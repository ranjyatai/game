using UnityEngine;

/// <summary>
/// 自动给场景里Tag为"Player"的角色挂上 SkyPrisonCharacterEnvironmentLightReceiver，
/// 不用手动在Editor里拖组件/拖引用——每次场景加载后跑一次，已经有这个组件的角色
/// 会跳过，不会重复添加。
/// </summary>
public static class SkyPrisonCharacterEnvironmentLightBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToPlayerCharacters()
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
}
