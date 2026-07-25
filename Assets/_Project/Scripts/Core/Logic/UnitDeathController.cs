using System;
using UnityEngine;
using Spine.Unity;

[DisallowMultipleComponent]
public class UnitDeathController : MonoBehaviour
{
    public enum DeathState
    {
        Alive,
        Dying,
        Dead,
        WaitingForRespawn,
        Cleaned
    }

    public enum CorpseCleanupMode
    {
        DisableObject,
        DestroyObject
    }

    [Header("基础")]
    [SerializeField] private bool autoFindOnAwake = true;
    [SerializeField] private UnitDefinitionRuntimeBinder runtimeBinder;
    [SerializeField] private Animator animator;

    [Header("死亡动画")]
    [SerializeField] private bool playDeathAnimation = true;
    [SerializeField] private string deathTriggerName = "Die";

    [Header("死亡后处理")]
    [SerializeField] private bool disableMovementOnDeath = true;
    [SerializeField] private bool disableCollidersOnDeath = true;
    [SerializeField] private bool freezeRigidbodyOnDeath = true;

    [Header("尸体保留")]
    [SerializeField] private bool useCorpseDecay = true;
    [SerializeField] private float corpseDecaySeconds = 8f;
    [SerializeField] private CorpseCleanupMode corpseCleanupMode = CorpseCleanupMode.DestroyObject;

    [Header("死亡溶解特效")]
    [SerializeField] private bool useDeathDissolve = true;
    [SerializeField] private float deathDissolveSeconds = 1.2f;
    [SerializeField] private Color deathDissolveEdgeColor = new Color(0.5f, 0.04f, 0.02f, 1f);
    [SerializeField] private float deathDissolveEdgeWidth = 0.12f;
    [SerializeField] private Texture2D deathDissolveNoiseTexture;
    [SerializeField] private float deathDissolveNoiseScale = 0.6f;
    [SerializeField] private float deathDissolveDarkenFraction = 0.35f;

    [Header("玩家专属")]
    [SerializeField] private bool treatAsRespawnablePlayer = false;

    [Header("调试")]
    [SerializeField] private bool debugLogs = false;
    [SerializeField] private DeathState currentState = DeathState.Alive;
    [SerializeField] private float runtimeDeathTimer = 0f;

    // 死亡前激活的 Collider 列表，复活时只恢复这些
    private Collider[] _collidersEnabledBeforeDeath;

    private static readonly int DissolveAmountId = Shader.PropertyToID("_SkyPrison_DissolveAmount");
    private static readonly int DissolveDarkenId = Shader.PropertyToID("_SkyPrison_DissolveDarken");
    private static readonly int DissolveNoiseTexId = Shader.PropertyToID("_SkyPrison_DissolveNoiseTex");
    private static readonly int DissolveNoiseScaleId = Shader.PropertyToID("_SkyPrison_DissolveNoiseScale");
    private static readonly int DissolveEdgeWidthId = Shader.PropertyToID("_SkyPrison_DissolveEdgeWidth");
    private static readonly int DissolveEdgeColorId = Shader.PropertyToID("_SkyPrison_DissolveEdgeColor");

    private static Texture2D s_DefaultDissolveNoise;

    private Renderer[] _dissolveRenderers;
    private MaterialPropertyBlock _dissolveBlock;
    private bool _dissolveStarted;
    private float _dissolveStartTimer;
    private float _dissolveEffectiveSeconds;

    public event Action<UnitDeathController> OnDeathStarted;
    public event Action<UnitDeathController> OnDeathFinished;
    public event Action<UnitDeathController> OnWaitingForRespawn;
    public event Action<UnitDeathController> OnRespawned;
    public event Action<UnitDeathController> OnCorpseCleaned;

    /// <summary>任意非玩家单位死亡时触发（供 SessionStatsTracker 统计击杀数）。</summary>
    public static event Action<UnitDeathController> OnAnyEnemyDied;

    public DeathState CurrentState => currentState;
    public bool IsAlive => currentState == DeathState.Alive;
    public bool IsDeadLike =>
        currentState == DeathState.Dying ||
        currentState == DeathState.Dead ||
        currentState == DeathState.WaitingForRespawn ||
        currentState == DeathState.Cleaned;

    public bool IsWaitingForRespawn => currentState == DeathState.WaitingForRespawn;
    public float CorpseDecaySeconds => corpseDecaySeconds;
    public UnitDefinition UnitDefinitionAsset => runtimeBinder != null ? runtimeBinder.UnitDefinitionAsset : null;

    private void Reset()
    {
        AutoSetup();
    }

    private void Awake()
    {
        if (autoFindOnAwake)
            AutoSetup();

        runtimeDeathTimer = 0f;
        currentState = DeathState.Alive;
        _dissolveStarted = false;
        ResetDissolveVisual();
    }

    private void Update()
    {
        if (currentState != DeathState.Dead)
            return;

        if (treatAsRespawnablePlayer)
            return;

        if (!useCorpseDecay)
            return;

        runtimeDeathTimer += Time.deltaTime;

        if (useDeathDissolve && !_dissolveStarted)
        {
            // 溶解时长不能超过尸体保留总时长，否则会在播完之前被CleanupCorpse截断，
            // 表现成"溶解到一半突然消失"。
            _dissolveEffectiveSeconds = Mathf.Min(Mathf.Max(0.01f, deathDissolveSeconds), corpseDecaySeconds);
            _dissolveStartTimer = Mathf.Max(0f, corpseDecaySeconds - _dissolveEffectiveSeconds);
            if (runtimeDeathTimer >= _dissolveStartTimer)
                StartDeathDissolve();
        }

        if (_dissolveStarted)
            UpdateDeathDissolve();

        if (runtimeDeathTimer >= corpseDecaySeconds)
            CleanupCorpse();
    }

    private void StartDeathDissolve()
    {
        _dissolveStarted = true;
        _dissolveRenderers = GetComponentsInChildren<Renderer>(true);
        if (_dissolveBlock == null)
            _dissolveBlock = new MaterialPropertyBlock();

        Texture2D noiseTex = deathDissolveNoiseTexture != null ? deathDissolveNoiseTexture : GetDefaultDissolveNoise();

        for (int i = 0; i < _dissolveRenderers.Length; i++)
        {
            Renderer r = _dissolveRenderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(_dissolveBlock);
            if (noiseTex != null)
                _dissolveBlock.SetTexture(DissolveNoiseTexId, noiseTex);
            _dissolveBlock.SetFloat(DissolveNoiseScaleId, deathDissolveNoiseScale);
            _dissolveBlock.SetFloat(DissolveEdgeWidthId, Mathf.Max(0.001f, deathDissolveEdgeWidth));
            _dissolveBlock.SetColor(DissolveEdgeColorId, deathDissolveEdgeColor);
            _dissolveBlock.SetFloat(DissolveAmountId, 0f);
            _dissolveBlock.SetFloat(DissolveDarkenId, 0f);
            r.SetPropertyBlock(_dissolveBlock);
        }
    }

    private void UpdateDeathDissolve()
    {
        if (_dissolveRenderers == null)
            return;

        float totalT = _dissolveEffectiveSeconds > 0.0001f
            ? Mathf.Clamp01((runtimeDeathTimer - _dissolveStartTimer) / _dissolveEffectiveSeconds)
            : 1f;

        // 两段式：先花 darkenFraction 比例的时间把本体压黑（不裁剪），
        // 压满之后剩下的时间才播放真正的噪波消失。
        float darkenFraction = Mathf.Clamp01(deathDissolveDarkenFraction);
        float darken;
        float amount;
        if (darkenFraction <= 0.0001f)
        {
            darken = 1f;
            amount = totalT;
        }
        else if (totalT <= darkenFraction)
        {
            darken = totalT / darkenFraction;
            amount = 0f;
        }
        else
        {
            darken = 1f;
            float remaining = 1f - darkenFraction;
            amount = remaining > 0.0001f ? (totalT - darkenFraction) / remaining : 1f;
        }

        for (int i = 0; i < _dissolveRenderers.Length; i++)
        {
            Renderer r = _dissolveRenderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(_dissolveBlock);
            _dissolveBlock.SetFloat(DissolveAmountId, amount);
            _dissolveBlock.SetFloat(DissolveDarkenId, darken);
            r.SetPropertyBlock(_dissolveBlock);
        }
    }

    private void ResetDissolveVisual()
    {
        if (_dissolveRenderers == null || _dissolveBlock == null)
        {
            _dissolveRenderers = null;
            return;
        }

        for (int i = 0; i < _dissolveRenderers.Length; i++)
        {
            Renderer r = _dissolveRenderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(_dissolveBlock);
            _dissolveBlock.SetFloat(DissolveAmountId, 0f);
            _dissolveBlock.SetFloat(DissolveDarkenId, 0f);
            r.SetPropertyBlock(_dissolveBlock);
        }

        _dissolveRenderers = null;
    }

    private static bool s_DissolveNoiseLoadAttempted;

    private static Texture2D GetDefaultDissolveNoise()
    {
        if (s_DefaultDissolveNoise != null || s_DissolveNoiseLoadAttempted)
            return s_DefaultDissolveNoise;

        s_DissolveNoiseLoadAttempted = true;

        // 贴图可能被 Unity 自动导入成 Sprite (2D and UI) 类型，此时 Resources.Load<Texture2D>
        // 会读到 null（资产的主对象是 Sprite，不是 Texture2D），要按 Sprite 再兜底读一次。
        s_DefaultDissolveNoise = Resources.Load<Texture2D>("VFX/Dissolve/SkyPrison_DissolveNoise");
        if (s_DefaultDissolveNoise == null)
        {
            Sprite sprite = Resources.Load<Sprite>("VFX/Dissolve/SkyPrison_DissolveNoise");
            if (sprite != null)
                s_DefaultDissolveNoise = sprite.texture;
        }

        if (s_DefaultDissolveNoise == null)
            Debug.LogWarning("[UnitDeathController] 找不到默认死亡溶解噪波贴图 Resources/VFX/Dissolve/SkyPrison_DissolveNoise，溶解特效会因为噪波贴图为纯白而不可见（除非单位定义单独指定了贴图）。");

        return s_DefaultDissolveNoise;
    }

    [ContextMenu("Auto Setup")]
    public void AutoSetup()
    {
        if (runtimeBinder == null)
            runtimeBinder = GetComponent<UnitDefinitionRuntimeBinder>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
    }

    public void ApplyDefinitionDefaults(
        bool newPlayDeathAnimation,
        string newDeathTriggerName,
        bool newUseCorpseDecay,
        float newCorpseDecaySeconds,
        CorpseCleanupMode newCleanupMode,
        bool newTreatAsRespawnablePlayer,
        bool newUseDeathDissolve = true,
        float newDeathDissolveSeconds = 1.2f,
        Color? newDeathDissolveEdgeColor = null,
        float newDeathDissolveEdgeWidth = 0.12f,
        Texture2D newDeathDissolveNoiseTexture = null,
        float newDeathDissolveNoiseScale = 0.6f,
        float newDeathDissolveDarkenFraction = 0.35f)
    {
        playDeathAnimation = newPlayDeathAnimation;
        deathTriggerName = newDeathTriggerName;
        useCorpseDecay = newUseCorpseDecay;
        corpseDecaySeconds = Mathf.Max(0f, newCorpseDecaySeconds);
        corpseCleanupMode = newCleanupMode;
        treatAsRespawnablePlayer = newTreatAsRespawnablePlayer;

        useDeathDissolve = newUseDeathDissolve;
        deathDissolveSeconds = Mathf.Max(0.01f, newDeathDissolveSeconds);
        deathDissolveEdgeColor = newDeathDissolveEdgeColor ?? deathDissolveEdgeColor;
        deathDissolveEdgeWidth = newDeathDissolveEdgeWidth;
        deathDissolveNoiseTexture = newDeathDissolveNoiseTexture;
        deathDissolveNoiseScale = newDeathDissolveNoiseScale;
        deathDissolveDarkenFraction = newDeathDissolveDarkenFraction;
    }

    [ContextMenu("Kill")]
    public void Kill()
    {
        if (currentState != DeathState.Alive)
            return;

        currentState = DeathState.Dying;
        runtimeDeathTimer = 0f;

        if (debugLogs)
            Debug.Log($"[UnitDeathController] {name}: Kill()", this);

        OnDeathStarted?.Invoke(this);

        // 非玩家单位死亡：通知统计系统
        if (!treatAsRespawnablePlayer)
            OnAnyEnemyDied?.Invoke(this);

        GetComponent<UnitActionController>()?.ForceDead();
        GetComponent<UnitStatusController>()?.ClearAllStatuses();
        TriggerDeathAnimation();
        ApplyDeathState();

        if (treatAsRespawnablePlayer)
        {
            currentState = DeathState.WaitingForRespawn;

            if (debugLogs)
                Debug.Log($"[UnitDeathController] {name}: WaitingForRespawn", this);

            OnWaitingForRespawn?.Invoke(this);
            return;
        }

        currentState = DeathState.Dead;
        OnDeathFinished?.Invoke(this);
    }

    public void SetTreatAsRespawnablePlayer(bool value) => treatAsRespawnablePlayer = value;

    /// <summary>玩家拒绝复活时调用：从 WaitingForRespawn 强制进入 Dead。</summary>
    public void FinalizeDeathFromRespawnWait()
    {
        if (currentState != DeathState.WaitingForRespawn)
            return;

        currentState = DeathState.Dead;
        runtimeDeathTimer = 0f;
        OnDeathFinished?.Invoke(this);
    }

    [ContextMenu("Cleanup Corpse")]
    public void CleanupCorpse()
    {
        if (currentState != DeathState.Dead)
            return;

        currentState = DeathState.Cleaned;

        if (debugLogs)
            Debug.Log($"[UnitDeathController] {name}: CleanupCorpse()", this);

        OnCorpseCleaned?.Invoke(this);

        switch (corpseCleanupMode)
        {
            case CorpseCleanupMode.DisableObject:
                gameObject.SetActive(false);
                break;

            case CorpseCleanupMode.DestroyObject:
                Destroy(gameObject);
                break;
        }
    }

    [ContextMenu("Respawn")]
    public void Respawn()
    {
        if (currentState != DeathState.WaitingForRespawn)
            return;

        RestoreAliveState();
        GetComponent<UnitActionController>()?.ForceAlive();
        runtimeDeathTimer = 0f;
        currentState = DeathState.Alive;
        _dissolveStarted = false;
        ResetDissolveVisual();

        if (debugLogs)
            Debug.Log($"[UnitDeathController] {name}: Respawn()", this);

        OnRespawned?.Invoke(this);
    }

    private void TriggerDeathAnimation()
    {
        if (!playDeathAnimation)
            return;

        // 优先 Spine 动画
        SkeletonAnimation spine = GetComponentInChildren<SkeletonAnimation>(true);
        if (spine != null)
        {
            string deathKey = deathTriggerName; // 默认用配置的名字
            if (runtimeBinder != null && runtimeBinder.UnitDefinitionAsset?.animationKeys != null)
                deathKey = runtimeBinder.UnitDefinitionAsset.animationKeys.deathKey;

            if (!string.IsNullOrWhiteSpace(deathKey))
            {
                try { spine.AnimationState.SetAnimation(0, deathKey, false); }
                catch (System.Exception e) { Debug.LogWarning($"[UnitDeathController] 死亡动画 '{deathKey}' 不存在，跳过。({e.Message})", this); }
                return;
            }
        }

        // fallback：Unity Animator
        if (animator != null && !string.IsNullOrWhiteSpace(deathTriggerName))
            animator.SetTrigger(deathTriggerName);
    }

    private void ApplyDeathState()
    {
        if (disableMovementOnDeath)
        {
            UnitMovementController move = GetComponent<UnitMovementController>()
                                       ?? GetComponentInParent<UnitMovementController>(true)
                                       ?? GetComponentInChildren<UnitMovementController>(true);
            if (move != null)
                move.enabled = false;

            // Motor 是独立组件，必须单独禁用，否则死亡后仍会 MovePosition 导致角色平移
            TerrainGroundMotorV5 motor = GetComponent<TerrainGroundMotorV5>()
                                      ?? GetComponentInParent<TerrainGroundMotorV5>(true)
                                      ?? GetComponentInChildren<TerrainGroundMotorV5>(true);
            if (motor != null)
                motor.enabled = false;

            // AI 行为也立即停止
            var ai = GetComponent<AIBehaviorPackageRuntimeController>()
                  ?? GetComponentInParent<AIBehaviorPackageRuntimeController>(true)
                  ?? GetComponentInChildren<AIBehaviorPackageRuntimeController>(true);
            if (ai != null)
                ai.enabled = false;
        }

        if (freezeRigidbodyOnDeath)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
        }

        if (disableCollidersOnDeath)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            // 只记录死前本来就是 enabled 的，复活时才恢复
            int enabledCount = 0;
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null && colliders[i].enabled) enabledCount++;

            _collidersEnabledBeforeDeath = new Collider[enabledCount];
            int idx = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null) continue;
                if (colliders[i].enabled)
                    _collidersEnabledBeforeDeath[idx++] = colliders[i];
                colliders[i].enabled = false;
            }
        }
    }

    private void RestoreAliveState()
    {
        if (disableMovementOnDeath)
        {
            UnitMovementController move = GetComponent<UnitMovementController>();
            if (move != null)
                move.enabled = true;

            TerrainGroundMotorV5 motor = GetComponent<TerrainGroundMotorV5>()
                                      ?? GetComponentInParent<TerrainGroundMotorV5>(true)
                                      ?? GetComponentInChildren<TerrainGroundMotorV5>(true);
            if (motor != null)
                motor.enabled = true;
        }

        if (freezeRigidbodyOnDeath)
        {
            Rigidbody rb = GetComponent<Rigidbody>();
            // TerrainGroundMotorV5 自己管理 isKinematic（始终保持 true，用 MovePosition 驱动）
            // 如果此处强制设 false 会让 Unity 物理与 Motor 同时争夺位置控制，产生抖动
            bool hasMotor = GetComponent<TerrainGroundMotorV5>() != null;
            if (rb != null && !hasMotor)
                rb.isKinematic = false;
        }

        if (disableCollidersOnDeath && _collidersEnabledBeforeDeath != null)
        {
            for (int i = 0; i < _collidersEnabledBeforeDeath.Length; i++)
            {
                if (_collidersEnabledBeforeDeath[i] != null)
                    _collidersEnabledBeforeDeath[i].enabled = true;
            }
            _collidersEnabledBeforeDeath = null;
        }
    }

}

