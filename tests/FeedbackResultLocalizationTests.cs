using System;
using System.Collections.Generic;
using BetterMultiplayer.Config;
using BetterMultiplayer.Diagnostics;
using Xunit;

namespace BetterMultiplayer.Tests;

/// <summary>
/// 反馈结果弹窗的文案覆盖。
///
/// 设置页上的反馈按钮原来点完什么都不显示（"点了没反应"那个 bug），
/// 修法是发完弹一个结果框。**漏一种状态的话，弹窗里会直接显示裸键名**
/// （比如 BETTERMULTIPLAYER-FEEDBACK_FAILED.body），比不弹还糟——
/// 所以这里按 <see cref="FeedbackSendStatus"/> 逐个核对。
/// </summary>
public sealed class FeedbackResultLocalizationTests
{
    [Fact]
    public void EverySendStatusHasABody()
    {
        Dictionary<string, string> entries = ConfigLocalization.BuildFeedbackEntries("zhs");
        List<string> missing = [];

        foreach (FeedbackSendStatus status in Enum.GetValues<FeedbackSendStatus>())
        {
            string key = ConfigLocalization.Prefix + FeedbackResultPopup.KeyFor(status) + ".body";
            if (!entries.ContainsKey(key))
                missing.Add($"{status} -> {key}");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void HeaderAndOkButtonExist()
    {
        Dictionary<string, string> entries = ConfigLocalization.BuildFeedbackEntries("zhs");

        Assert.True(entries.ContainsKey(ConfigLocalization.Prefix + "FEEDBACK_RESULT.header"));
        Assert.True(entries.ContainsKey(ConfigLocalization.Prefix + "FEEDBACK_RESULT.ok"));
    }

    /// <summary>
    /// 弹窗文案和配置键走的是同一个 settings_ui 表，但分成两个方法。
    /// 这条守着"配置键那组不含弹窗键"——混进去会让 NoOrphanEntries 误报孤儿键。
    /// </summary>
    [Fact]
    public void ConfigEntriesDoNotContainFeedbackPopupKeys()
    {
        Dictionary<string, string> config = ConfigLocalization.BuildEntries("zhs");

        foreach (string key in ConfigLocalization.BuildFeedbackEntries("zhs").Keys)
            Assert.False(config.ContainsKey(key), $"{key} 不该出现在配置键那组里。");
    }

    [Theory]
    [InlineData("zhs")]
    [InlineData("eng")]
    public void EntriesAreNonEmpty(string language)
    {
        foreach ((string key, string value) in ConfigLocalization.BuildFeedbackEntries(language))
            Assert.False(string.IsNullOrWhiteSpace(value), $"{language} 的 {key} 是空的");
    }
}
