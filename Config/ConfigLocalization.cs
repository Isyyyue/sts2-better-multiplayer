using System.Collections.Generic;
using BetterMultiplayer.Diagnostics;
using BetterMultiplayer.Localization;
using MegaCrit.Sts2.Core.Localization;

namespace BetterMultiplayer.Config;

/// <summary>
/// 把设置页需要的文案装进游戏的 settings_ui 表。
///
/// BaseLib 的键名规则（BaseLib.Config.ModConfig.GetLabelText 与
/// BaseLib.Config.UI.NConfigOptionRow.AddHoverTip）：
///
///     行标题      {ModPrefix}{Slugify(属性名)}.title
///     悬停标题    {ModPrefix}{Slugify(属性名)}.hover.title
///     悬停说明    {ModPrefix}{Slugify(属性名)}.hover.desc
///     分组标题    {ModPrefix}{Slugify(分组名)}.title
///
/// 其中 ModPrefix = 命名空间第一段大写 + "-"
/// （BaseLib.Extensions.TypePrefix.GetPrefix），
/// 本类命名空间是 BetterMultiplayer.Config，所以前缀是 "BETTERMULTIPLAYER-"。
/// Slugify 把 CamelCase 拆成下划线并转大写（StringHelper.Slugify）。
///
/// 键名写错不会报错——BaseLib 会静默回落到原始属性名，
/// 界面上直接显示 "AllowUsedUpRelics" 这种英文标识符。
/// 所以 <see cref="BuildEntries"/> 保持纯函数，由 ConfigLocalizationTests
/// 拿真实的 Slugify 逐个核对。
/// </summary>
internal static class ConfigLocalization
{
    internal const string Prefix = "BETTERMULTIPLAYER-";
    internal const string Table = "settings_ui";

    /// <summary>设置页用到的全部文案。language 为游戏的语言代码。</summary>
    internal static Dictionary<string, string> BuildEntries(string language) => new()
    {
        // 分组标题（总开关没有分组，见 BetterMultiplayerConfig 的说明）
        [Prefix + "FEEDBACK.title"] = Text(language, TextKey.SettingFeedbackSection),

        // 总开关。设置页上【不分类】，所以这里只有这一个分项。
        // 悬停说明替代了原来那些分类说明——分类被砍掉了，但"打开后会放开什么"
        // 仍然得讲清楚，不然玩家不知道自己在开什么。
        [Prefix + "UNLOCK_RELIC_TRADING.title"] = Text(language, TextKey.SettingUnlockRelicTrading),
        [Prefix + "UNLOCK_RELIC_TRADING.hover.title"] =
            Text(language, TextKey.SettingUnlockRelicTrading),
        [Prefix + "UNLOCK_RELIC_TRADING.hover.desc"] =
            Text(language, TextKey.SettingUnlockRelicTradingTip),

        // 惊喜模式。刻意没有分组标题键，也没有 .hover.* 键——
        // 「不给任何解释」是功能的一部分，加回去会被 SurpriseModeTests 挡下。
        [Prefix + "SURPRISE_SHARED_GOLD.title"] = Text(language, TextKey.SettingSurpriseMode),

        // 反馈按钮行。BaseLib 的按钮行有【两个】标题键：
        //   行标题   Slugify(方法名)           -> SEND_FEEDBACK.title
        //   按钮文字 Slugify(ButtonLabelKey)  -> SEND_FEEDBACK_BUTTON.title
        // 只写后者的话，行标题会静默回落成原始方法名，界面上直接显示 "SendFeedback"。
        // 悬停键同样取自方法名（NConfigOptionRow.AddHoverTip 用的是行名）。
        [Prefix + "SEND_FEEDBACK.title"] = Text(language, TextKey.SendFeedback),
        [Prefix + "SEND_FEEDBACK.hover.title"] = Text(language, TextKey.SendFeedback),
        [Prefix + "SEND_FEEDBACK.hover.desc"] = Text(language, TextKey.SendFeedbackTooltip),
        [Prefix + "SEND_FEEDBACK_BUTTON.title"] = Text(language, TextKey.SettingSendFeedbackButton)
    };

    /// <summary>
    /// 反馈结果弹窗的文案。刻意和 <see cref="BuildEntries"/> 分开：
    ///
    ///   1. 这组键不是从配置属性推导出来的，混进去会让
    ///      ConfigLocalizationTests.NoOrphanEntries 把它们当成孤儿键报错。
    ///   2. 它们的"覆盖率"判据不一样——那边是"每个配置项都有键"，
    ///      这边是"每种发送状态都有键"（见 FeedbackEntriesCoverEveryStatus）。
    /// </summary>
    internal static Dictionary<string, string> BuildFeedbackEntries(string language) => new()
    {
        [Prefix + "FEEDBACK_RESULT.header"] = Text(language, TextKey.FeedbackResultHeader),
        [Prefix + "FEEDBACK_RESULT.ok"] = Text(language, TextKey.FeedbackResultOk),
        [Prefix + FeedbackResultPopup.KeyFor(FeedbackSendStatus.Submitted) + ".body"] =
            Text(language, TextKey.FeedbackSubmitted),
        [Prefix + FeedbackResultPopup.KeyFor(FeedbackSendStatus.Busy) + ".body"] =
            Text(language, TextKey.FeedbackBusy),
        [Prefix + FeedbackResultPopup.KeyFor(FeedbackSendStatus.RateLimited) + ".body"] =
            Text(language, TextKey.FeedbackRateLimited),
        [Prefix + FeedbackResultPopup.KeyFor(FeedbackSendStatus.NetworkFailed) + ".body"] =
            Text(language, TextKey.FeedbackFailed)
    };

    internal static void Install(LocManager locManager, string language)
    {
        locManager.GetTable(Table).MergeWith(BuildEntries(language));
        locManager.GetTable(Table).MergeWith(BuildFeedbackEntries(language));
    }

    private static string Text(string language, TextKey key) =>
        ModText.ForLanguage(language, key);
}
