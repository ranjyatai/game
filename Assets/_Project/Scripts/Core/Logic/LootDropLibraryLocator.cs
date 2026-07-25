using UnityEngine;

/// <summary>
/// 挂到场景里任意持久 GameObject（如 GameManager），
/// 把 LootDropModelLibrary 资产拖进 library 字段。
/// Build 里 Resources.Load 因文件路径问题失效时，
/// 此组件在 Awake 直接注入引用，无需资产在 Resources 文件夹内。
/// </summary>
[DefaultExecutionOrder(-500)]
public class LootDropLibraryLocator : MonoBehaviour
{
    [SerializeField] private LootDropModelLibrary library;

    private void Awake()
    {
        if (library != null)
            LootDropModelLibrary.RegisterInstance(library);
        else
            Debug.LogError("[LootDropLibraryLocator] library 字段为空！请在 Inspector 拖入 LootDropModelLibrary 资产。", this);
    }
}
