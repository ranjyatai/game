using UnityEngine;

/// <summary>
/// 自动给场景里Tag为"Player"的角色挂上 SkyPrisonCharacterEnvironmentLightReceiver，
/// 不用手动在Editor里拖组件/拖引用，已经有这个组件的角色会跳过，不会重复添加。
///
/// 为什么不是只在 AfterSceneLoad 跑一次：那个回调只在「进 Play 的第一个场景」之后触发。
/// 从主菜单 / 载入画面进到地图（或者玩家是后来才生成的）时它早就跑完了，
/// 玩家身上就永远没有接收器——没有环境受光、没有灯光轮廓投影，角色是死白的。
/// 另外 UnitDefinitionRuntimeApplier 只给"套用过定义"的单位自动挂接收器，玩家的 Applier 没跑过就不会挂。
/// 所以这里再起一个常驻的小轮询器：每半秒查一次所有 Player 标签的物体，缺了就补。
/// </summary>
public static class SkyPrisonCharacterEnvironmentLightBootstrap
{
    private static GameObject s_runner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_runner = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPlayerCharacters()
    {
        AttachNow();
        EnsureRunner();
    }

    private static void EnsureRunner()
    {
        if (s_runner != null)
            return;

        // 编辑器里关掉 Domain Reload 进 Play 时，上一轮留下的同名物体要清掉，不然越积越多。
        foreach (var existing in Resources.FindObjectsOfTypeAll<Poller>())
        {
            if (existing != null)
                Object.Destroy(existing.gameObject);
        }

        s_runner = new GameObject("[SkyPrisonEnvLightBootstrap]");
        Object.DontDestroyOnLoad(s_runner);
        s_runner.AddComponent<Poller>();
    }

    private static void AttachNow()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        for (int i = 0; i < players.Length; i++)
        {
            GameObject player = players[i];
            if (player == null) continue;
            if (player.GetComponent<SkyPrisonCharacterEnvironmentLightReceiver>() != null) continue;

            player.AddComponent<SkyPrisonCharacterEnvironmentLightReceiver>();
        }
    }

    private class Poller : MonoBehaviour
    {
        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 0.5f;
            AttachNow();
        }
    }
}
