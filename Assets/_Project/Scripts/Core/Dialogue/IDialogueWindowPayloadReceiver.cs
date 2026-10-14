/// <summary>
/// 让"打开窗口"这类通用动作能带上"打开哪个具体数据"的信息——比如同样是打开
/// "商店"窗口，供给站、古董店、工坊商店这几个不同的 ShopDefinition 得能分开指定，
/// 不能只靠窗口prefab自己写死一个。以后设施升级窗口之类需要"指定哪个具体配置"的
/// 窗口都实现这个接口即可，不需要为每种窗口单独扩展 DialogueOption 的字段。
/// </summary>
public interface IDialogueWindowPayloadReceiver
{
    void ReceiveDialoguePayload(UnityEngine.Object payload);
}
