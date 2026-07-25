using UnityEngine;

/// <summary>
/// 报错自动上报的 Discord Webhook 地址，单独放一个 ScriptableObject，不写死在代码里——
/// 这串 URL 等同于密钥，任何人拿到都能往你的 Discord 频道发消息，以后要换掉（比如
/// Webhook 泄露了）只需要改这个资产，不用碰任何 .cs 文件。
/// 建议：这个资产文件本身不要发布/分享出去，团队协作时也不要直接提交明文到公共仓库。
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/Diagnostics/Error Reporting Settings", fileName = "SkyPrisonErrorReportingSettings")]
public class SkyPrisonErrorReportingSettings : ScriptableObject
{
    [Tooltip("Discord 频道的 Webhook URL。留空则不自动上报，只写本地日志。")]
    public string discordWebhookUrl = "";
}
