using System.Collections;
using UnityEngine;

/// <summary>
/// 操作设置子面板。
/// 直接读写 SkyPrisonInputSettings asset（它本身就是权威数据源，不走 SettingsData）。
/// 键位重映射：点击行 → 进入捕获模式 → 按下任意键 → 写回 binding。
/// </summary>
public class SettingsControlsPanel : MonoBehaviour
{
    [Tooltip("项目 InputSettings asset（Data/Settings/SkyPrisonInputSettings.asset）")]
    [SerializeField] private SkyPrisonInputSettings inputSettings;

    // ── 捕获状态 ──────────────────────────────────────────────────────────
    public bool IsCapturing { get; private set; }

    private SkyPrisonInputBinding _capturingBinding;
    private KeyTarget             _capturingTarget;
    private Coroutine             _captureRoutine;

    public enum KeyTarget { Primary, Secondary, Gamepad }

    public void SetVisible(bool v) => gameObject.SetActive(v);

    public void Refresh()
    {
        // inputSettings 是 asset，本身就是最新值，UI 绑定时直接读 inputSettings.bindings
        RefreshUI();
    }

    // Apply 对键位设置是空操作：写已经在捕获时实时发生
    public void Apply() { }

    // ── 公开接口：UI 行点击后调用 ─────────────────────────────────────────

    /// <summary>开始捕获指定 binding 的某个键位槽。</summary>
    public void BeginCapture(SkyPrisonInputBinding binding, KeyTarget target)
    {
        if (IsCapturing) StopCapture();
        _capturingBinding = binding;
        _capturingTarget  = target;
        _captureRoutine   = StartCoroutine(CaptureRoutine());
    }

    /// <summary>清空指定槽位（设为 KeyCode.None）。</summary>
    public void ClearKey(SkyPrisonInputBinding binding, KeyTarget target)
    {
        WriteKey(binding, target, KeyCode.None);
        SaveInputSettings();
        RefreshUI();
    }

    /// <summary>将所有 bindings 恢复默认值。</summary>
    public void ResetToDefaults()
    {
        if (inputSettings == null) return;
        inputSettings.EnsureDefaults();
        SaveInputSettings();
        RefreshUI();
    }

    // ── 捕获协程 ──────────────────────────────────────────────────────────

    private IEnumerator CaptureRoutine()
    {
        IsCapturing = true;
        OnCaptureStateChanged(true);

        // 等一帧，避免触发 Capture 的那次按下被立刻捕获
        yield return null;

        float timeout = 8f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            elapsed += Time.unscaledDeltaTime;

            // 取消捕获
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                break;
            }

            // 遍历所有 KeyCode 检测按下
            foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.Escape) continue;
                if (!Input.GetKeyDown(kc)) continue;

                // 检查冲突
                if (HasConflict(kc, _capturingBinding.action))
                {
                    OnConflictDetected(kc);
                    goto EndCapture;
                }

                WriteKey(_capturingBinding, _capturingTarget, kc);
                SaveInputSettings();
                goto EndCapture;
            }

            yield return null;
        }

        EndCapture:
        IsCapturing = false;
        _captureRoutine = null;
        OnCaptureStateChanged(false);
        RefreshUI();
    }

    private void StopCapture()
    {
        if (_captureRoutine != null) StopCoroutine(_captureRoutine);
        _captureRoutine = null;
        IsCapturing     = false;
        OnCaptureStateChanged(false);
    }

    // ── 冲突检测 ──────────────────────────────────────────────────────────

    private bool HasConflict(KeyCode kc, SkyPrisonInputAction excludeAction)
    {
        if (inputSettings == null) return false;
        foreach (var b in inputSettings.bindings)
        {
            if (b.action == excludeAction) continue;
            if (b.primaryKey == kc || b.secondaryKey == kc || b.gamepadKey == kc)
                return true;
        }
        return false;
    }

    // ── 写键值 ────────────────────────────────────────────────────────────

    private static void WriteKey(SkyPrisonInputBinding binding, KeyTarget target, KeyCode kc)
    {
        switch (target)
        {
            case KeyTarget.Primary:   binding.primaryKey   = kc; break;
            case KeyTarget.Secondary: binding.secondaryKey = kc; break;
            case KeyTarget.Gamepad:   binding.gamepadKey   = kc; break;
        }
    }

    // ── 持久化：写回 asset ────────────────────────────────────────────────

    private void SaveInputSettings()
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(inputSettings);
        UnityEditor.AssetDatabase.SaveAssets();
#endif
        // 运行时：asset 修改在内存里即时生效，SkyPrisonInputSettings 的 lookup 需要重建
        inputSettings?.RebuildLookup();
    }

    // ── UI 回调（做 UI 时在这里 push 数据到控件）─────────────────────────

    private void RefreshUI()
    {
        // TODO: 键位列表 ScrollView 绑定后在这里重建每一行的显示
    }

    private void OnCaptureStateChanged(bool capturing)
    {
        // TODO: 高亮正在等待输入的行，显示「请按任意键…」提示
    }

    private void OnConflictDetected(KeyCode kc)
    {
        // TODO: 弹出冲突提示（「此键已被 XX 使用」）
        Debug.LogWarning($"[Settings] 键位冲突：{kc} 已被其他操作使用。");
    }
}
