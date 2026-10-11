using UnityEngine;

/// <summary>
/// 开发测试快捷键(只在编辑器 / Development Build 里存在，正式包不编译进去)：
///   F5  给 500 AFP(不在一局里就先开一局)——测 AFP 购买窗口 / 抵消盾用
///   F8  切换「扩容弹匣折扣」(科技树节点还没建好时用来看打折后的价格 350→300)
///   F7  切换「抵消盾跳过科技树解锁检查」(科技树的探索者节点建好之前用来验证盾)
/// </summary>
static class SkyPrisonDevHotkeys
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    sealed class Runner : MonoBehaviour
    {
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F5))
            {
                var afp = AFPRuntime.Instance;
                if (afp == null) { Debug.LogWarning("[DevHotkey] F5：AFPRuntime 还没就绪"); return; }
                if (!afp.IsInSession) afp.BeginSession();
                afp.AddAFP(500);
                Debug.Log("[DevHotkey] F5：+500 AFP，当前 " + afp.CurrentAFP);
            }
            if (Input.GetKeyDown(KeyCode.F8) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                SkyPrisonScreenBanner.Show("战术研修解放");       // Ctrl+F8：预览「战术研修解放」横带
            }
            else if (Input.GetKeyDown(KeyCode.F8))
            {
                SkyPrisonAfpUpgrades.DebugMagazineDiscount = !SkyPrisonAfpUpgrades.DebugMagazineDiscount;
                SkyPrisonAfpUpgrades.ClearPending();      // 触发窗口刷新价格
                Debug.Log("[DevHotkey] F8：扩容弹匣折扣 = " + SkyPrisonAfpUpgrades.DebugMagazineDiscount);
            }
            if (Input.GetKeyDown(KeyCode.F7))
            {
                SkyPrisonAfpAbsorbShield.DebugForceUnlock = !SkyPrisonAfpAbsorbShield.DebugForceUnlock;
                Debug.Log("[DevHotkey] F7：抵消盾跳过科技树解锁 = " + SkyPrisonAfpAbsorbShield.DebugForceUnlock);
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("[DevHotkeys]") { hideFlags = HideFlags.HideInHierarchy };
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Runner>();
    }
#endif
}
