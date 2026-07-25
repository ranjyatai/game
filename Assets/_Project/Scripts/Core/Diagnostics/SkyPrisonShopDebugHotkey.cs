using SkyPrison.Runtime.UI;
using UnityEngine;

/// <summary>
/// 按 F6 直接打开/关闭商店窗口（演示数据已经烤进prefab的shopDefinition字段），纯粹
/// 用来在没有交互式商店NPC/设施的情况下调试界面。只在编辑器/开发版里编译，正式发布版
/// （非 Development Build）里这段代码根本不存在。仓库那份同名调试键用完之后已经删掉了，
/// 商店这份等定稿后同样要删。
/// </summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
public static class SkyPrisonShopDebugHotkey
{
    private const string ShopResourcesPath = "UI/Window/PF_SkyPrisonShop";
    private static bool _installed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (_installed) return;
        _installed = true;

        var go = new GameObject("[ShopDebugHotkey]") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Ticker>();
    }

    private class Ticker : MonoBehaviour
    {
        private GameObject _shopPrefab;

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F6)) return;

            var manager = Object.FindObjectOfType<SkyPrisonWindowManager_V1>();
            if (manager == null)
            {
                Debug.LogWarning("[ShopDebugHotkey] 场景里找不到 SkyPrisonWindowManager_V1，无法开关商店窗口。");
                return;
            }

            if (manager.IsOpen("shop_demo_shop"))
            {
                manager.Close("shop_demo_shop");
                return;
            }

            if (_shopPrefab == null)
                _shopPrefab = Resources.Load<GameObject>(ShopResourcesPath);

            if (_shopPrefab == null)
            {
                Debug.LogWarning($"[ShopDebugHotkey] 找不到商店窗口 prefab（Resources/{ShopResourcesPath}）——" +
                    "先在编辑器菜单 Tools/Sky Prison/UI/Create Shop Window 生成一次。");
                return;
            }

            SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Open);
            manager.Open(_shopPrefab);
        }
    }
}
#endif
