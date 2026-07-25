using UnityEngine;

[DisallowMultipleComponent]
public class PlayerRespawnHandler : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private UnitDeathController deathController;

    [Header("复活设置")]
    [SerializeField] private Transform respawnPoint;
    [SerializeField] private bool respawnAtPoint = true;
    [SerializeField] private Vector3 respawnOffset = Vector3.zero;

    [Header("调试")]
    [SerializeField] private bool debugLogs = false;

    private void Reset()
    {
        AutoSetup();
    }

    private void Awake()
    {
        AutoSetup();
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (deathController == null)
            deathController = GetComponent<UnitDeathController>();
    }

    [ContextMenu("Respawn Now")]
    public void RespawnNow()
    {
        if (deathController == null)
            return;

        if (!deathController.IsWaitingForRespawn)
            return;

        if (respawnAtPoint && respawnPoint != null)
            transform.position = respawnPoint.position + respawnOffset;

        deathController.Respawn();

        if (debugLogs)
            Debug.Log($"[PlayerRespawnHandler] {name}: RespawnNow()", this);
    }

    public void SetRespawnPoint(Transform point)
    {
        respawnPoint = point;
    }
}
