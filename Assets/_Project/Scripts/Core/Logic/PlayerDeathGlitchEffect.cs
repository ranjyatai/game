using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDeathGlitchEffect : MonoBehaviour
{
    [SerializeField] private float fadeInDuration  = 0.4f;
    [SerializeField] private float targetIntensity = 0.72f;
    [SerializeField] private float fadeOutDuration = 1.2f;

    private static readonly int _IntensityId = Shader.PropertyToID("_DeathGlitchIntensity");

    private UnitDeathController _death;
    private Coroutine           _routine;

    private void Awake()
    {
        _death = GetComponent<UnitDeathController>()
              ?? GetComponentInParent<UnitDeathController>()
              ?? GetComponentInChildren<UnitDeathController>();

        if (_death == null)
            Debug.LogWarning("[PlayerDeathGlitchEffect] 找不到 UnitDeathController，故障效果将不会触发。", this);

        Shader.SetGlobalFloat(_IntensityId, 0f);
    }

    private void OnEnable()
    {
        if (_death == null) return;
        _death.OnDeathStarted      += HandleDeathStarted;
        _death.OnWaitingForRespawn += HandleDeathStarted;
        _death.OnDeathFinished     += HandleDeathStarted;
        _death.OnRespawned         += HandleRespawned;
    }

    private void OnDisable()
    {
        if (_death == null) return;
        _death.OnDeathStarted      -= HandleDeathStarted;
        _death.OnWaitingForRespawn -= HandleDeathStarted;
        _death.OnDeathFinished     -= HandleDeathStarted;
        _death.OnRespawned         -= HandleRespawned;
    }

    private void HandleDeathStarted(UnitDeathController _)
    {
        Debug.Log("[PlayerDeathGlitchEffect] 死亡事件触发，开始淡入故障效果");
        Fade(targetIntensity, fadeInDuration);
    }

    private void HandleRespawned(UnitDeathController _) => FadeOut();

    // UI 关闭时也可从外部调用（复活/放弃死亡均需淡出）
    public void FadeOut() => Fade(0f, fadeOutDuration);

    private void Fade(float to, float dur)
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FadeRoutine(Shader.GetGlobalFloat(_IntensityId), to, dur));
    }

    private IEnumerator FadeRoutine(float from, float to, float dur)
    {
        if (dur <= 0f)
        {
            Shader.SetGlobalFloat(_IntensityId, to);
            _routine = null;
            yield break;
        }

        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float v = Mathf.Lerp(from, to, t / dur);
            Shader.SetGlobalFloat(_IntensityId, v);
            yield return null;
        }
        Shader.SetGlobalFloat(_IntensityId, to);
        _routine = null;
    }

    private void OnDestroy()
    {
        Shader.SetGlobalFloat(_IntensityId, 0f);
    }
}
