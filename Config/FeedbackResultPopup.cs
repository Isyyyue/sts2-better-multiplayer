using System;
using System.Threading.Tasks;
using BetterMultiplayer.Diagnostics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace BetterMultiplayer.Config;

/// <summary>
/// 把一次诊断反馈的结果弹给玩家看。
///
/// 为什么需要它：设置页上的反馈按钮原来只调
/// <see cref="DiagnosticFeedbackService.SendAsync"/>，那个方法把结果写进日志就完了
/// ——玩家点完屏幕上什么都不变，表现就是"点了没反应"。
/// （大厅那个入口本来有一段状态文字，入口删掉之后就彻底没有可见反馈了。）
///
/// 用游戏自己的 <see cref="NGenericPopup"/>，和 BaseLib「恢复默认」的确认框是同一套；
/// noButton 传 null 就是单按钮提示框。
///
/// 拿不到弹窗时只记日志——"结果没弹出来"不能反过来影响别的东西。
/// </summary>
internal static class FeedbackResultPopup
{
    internal static async Task ShowAsync(FeedbackSendResult result)
    {
        NGenericPopup? popup = NGenericPopup.Create();
        NModalContainer? modal = NModalContainer.Instance;
        if (popup is null || modal is null)
        {
            BetterMultiplayerMod.Logger.Warn(
                $"Could not show the feedback result popup (status={result.Status}).");
            return;
        }

        try
        {
            modal.Add(popup);
            await popup.WaitForConfirmation(
                Body(result.Status),
                Entry("FEEDBACK_RESULT.header"),
                null,
                Entry("FEEDBACK_RESULT.ok"));
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Warn(
                $"Showing the feedback result popup failed: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// 每种发送状态对应的文案键（不含前缀和后缀）。
    /// internal 是为了让测试能遍历 <see cref="FeedbackSendStatus"/> 核对覆盖率——
    /// 漏一种状态的话，玩家会看到弹窗里写着裸键名。
    /// </summary>
    internal static string KeyFor(FeedbackSendStatus status) => status switch
    {
        FeedbackSendStatus.Submitted => "FEEDBACK_SUBMITTED",
        FeedbackSendStatus.Busy => "FEEDBACK_BUSY",
        FeedbackSendStatus.RateLimited => "FEEDBACK_RATE_LIMITED",
        _ => "FEEDBACK_FAILED"
    };

    private static LocString Body(FeedbackSendStatus status) => Entry(KeyFor(status) + ".body");

    private static LocString Entry(string suffix) =>
        new(ConfigLocalization.Table, ConfigLocalization.Prefix + suffix);
}
