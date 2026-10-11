using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 控制远处门的开关（拉杆/按钮/面板）。玩家按交互键，对它绑定的每一扇门下一条命令。
/// 绑定在这里（开关 → 门），摆放工具里选中门时能看到是哪些开关在控制它，也能给它加开关。
/// 被控制的门，触发方式应设为"由远处的开关控制"。
/// </summary>
[DisallowMultipleComponent]
public sealed class SkyPrisonDoorSwitch : MonoBehaviour, IInteractable
{
    [Header("绑定的门")]
    public List<SkyPrisonSlidingDoor> doors = new List<SkyPrisonSlidingDoor>();

    [Header("行为")]
    [SerializeField] private DoorCommand command = DoorCommand.Toggle;
    [Tooltip("命令=打开一段时间后自动关 时的秒数。")]
    [SerializeField, Min(0.5f)] private float seconds = 8f;
    [Tooltip("按下后同时解开这些门的锁。")]
    [SerializeField] private bool unlockDoors = false;
    [Tooltip("只能按一次。")]
    [SerializeField] private bool singleUse = false;

    [Header("提示与锁")]
    [Tooltip("自定义提示文字，留空 = 多语言表默认文字（switch_interact_operate）。")]
    [SerializeField] private DoorLocalizedLabel label = new DoorLocalizedLabel();
    [SerializeField] private bool locked = false;
    [Tooltip("解锁条件，全部满足才能操作。不加 = 不限制。")]
    [SerializeField] private System.Collections.Generic.List<DoorCondition> unlockConditions = new System.Collections.Generic.List<DoorCondition>();
    [Tooltip("多条条件之间的关系：全部满足（与）/ 任一满足（或）。")]
    [SerializeField] private DoorConditionMatch conditionMatch = DoorConditionMatch.All;
    [Tooltip("留空 = 多语言表默认文字（switch_interact_locked）。")]
    [SerializeField] private DoorLocalizedLabel lockedLabel = new DoorLocalizedLabel();

    [Header("声音")]
    [SerializeField] private DoorAudioProfile audioProfile;

    private bool _used;
    private AudioSource _source;

    public Vector3 InteractPosition => transform.position;
    public string InteractLabel => IsLockedNow
        ? lockedLabel.Resolve("switch_interact_locked", "无法操作")
        : label.Resolve("switch_interact_operate", "操作");
    public bool CanInteract => !_used || !singleUse;

    private bool IsLockedNow =>
        locked || !DoorCondition.AllMet(unlockConditions, conditionMatch);

    public void Interact()
    {
        if (singleUse && _used) return;
        if (IsLockedNow) { PlayOne(audioProfile != null ? audioProfile.denied : null); return; }
        PlayOne(audioProfile != null ? audioProfile.interact : null);
        _used = true;
        foreach (var d in doors)
            if (d != null) d.ApplyCommand(command, seconds, unlockDoors);
    }

    private void OnEnable() { InteractableRegistry.Register(this); }
    private void OnDisable() { InteractableRegistry.Unregister(this); }

    private void PlayOne(DoorAudioProfile.Entry[] entries)
    {
        if (audioProfile == null || entries == null || entries.Length == 0) return;
        var e = entries[Random.Range(0, entries.Length)];
        if (e == null || e.clip == null) return;
        if (_source == null)
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = audioProfile.spatial ? 1f : 0f;
            _source.minDistance = audioProfile.minDistance;
            _source.maxDistance = Mathf.Max(audioProfile.maxDistance, audioProfile.minDistance + 0.1f);
            _source.rolloffMode = AudioRolloffMode.Linear;
        }
        var gs = SkyPrisonAudioGlobalSettings.Instance;
        float g = (gs != null ? gs.masterVolume * gs.seVolume : 1f) * audioProfile.masterVolume;
        _source.pitch = 1f + Random.Range(-e.pitchJitter, e.pitchJitter);
        _source.PlayOneShot(e.clip, g * Mathf.Max(0f, e.volume));
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
        foreach (var d in doors)
            if (d != null) Gizmos.DrawLine(transform.position + Vector3.up * 1.5f, d.transform.position + Vector3.up * 4f);
    }
}
