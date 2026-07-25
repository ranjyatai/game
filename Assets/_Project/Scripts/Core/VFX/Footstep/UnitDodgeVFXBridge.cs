using UnityEngine;

/// <summary>
/// 轮询 UnitMovementController 闪避状态，闪避起手瞬间在角色脚下生成一次扬尘。
/// 挂在角色根对象上，由 UnitDefinitionRuntimeApplier 自动添加。
/// </summary>
[DisallowMultipleComponent]
public class UnitDodgeVFXBridge : MonoBehaviour
{
    [SerializeField] private UnitMovementController movementController;

    [Tooltip("扬尘特效的基础缩放。默认调大到3倍，闪避这种大幅位移动作扬尘太小根本看不清。")]
    [SerializeField] private float dustScale = 3f;

    [Tooltip("扬尘特效存在时长（秒），播完自动回收。")]
    [SerializeField] private float dustLifetime = 3f;

    private UnitMovementController.DodgeRuntimeState _lastState = UnitMovementController.DodgeRuntimeState.None;

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
    }

    private void Update()
    {
        if (movementController == null)
            return;

        UnitMovementController.DodgeRuntimeState current = movementController.CurrentDodgeRuntimeState;

        if (current != _lastState)
        {
            if (current != UnitMovementController.DodgeRuntimeState.None && FootstepVFXManager.Instance != null)
            {
                FootstepVFXManager.Instance.SpawnDust(
                    FootstepVFXManager.Instance.DodgeDustPrefabs,
                    transform.position,
                    Quaternion.identity,
                    dustScale,
                    GetUnitSortingOrder(),
                    dustLifetime);
            }

            _lastState = current;
        }
    }

    private int GetUnitSortingOrder()
    {
        return Mathf.RoundToInt(transform.position.z * 100f);
    }

    public void Configure(UnitMovementController controller)
    {
        movementController = controller;
    }
}
