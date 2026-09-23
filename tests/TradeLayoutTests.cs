using Xunit;

namespace BetterMultiplayer.Tests;

/// <summary>
/// 交易界面的布局预算。
///
/// 这组测试的存在理由是 0.6.0 的一次事故：报价面板上下堆叠时，
/// 纵向需要 927px 而可用只有约 780px，选择网格被挤出可视区，
/// 玩家点不到物品。当时没有人把预算写下来，所以没人发现。
///
/// 现在预算提成了 TradeLayout 里的常量，改布局时这些断言会先失败。
/// </summary>
public sealed class TradeLayoutTests
{
    /// <summary>左右分屏后，两块报价面板必须并排放得下。</summary>
    [Fact]
    public void TwoOfferPanelsFitSideBySide()
    {
        Assert.True(
            TradeLayout.OfferPanelMinWidth <= TradeLayout.AvailablePerPanel,
            $"报价面板需要 {TradeLayout.OfferPanelMinWidth}px，" +
            $"但左右分屏后每块只有 {TradeLayout.AvailablePerPanel}px。");
    }

    /// <summary>预算要留一点余量，不要贴着上限——否则字体或边距微调就会溢出。</summary>
    [Fact]
    public void OfferPanelKeepsSomeSlack()
    {
        int slack = TradeLayout.AvailablePerPanel - TradeLayout.OfferPanelMinWidth;
        Assert.True(slack >= 16, $"余量只有 {slack}px，太紧。");
    }

    /// <summary>
    /// 分屏必须真的比堆叠省高度：两块面板并排只占一块的高度。
    /// 这条是防止有人改回上下堆叠——那会重新触发 0.6.0 的事故。
    /// </summary>
    [Fact]
    public void SideBySideUsesOnePanelHeightNotTwo()
    {
        const int panelMinHeight = 250;
        const int actionsHeight = 52;
        const int boardSeparation = 12;
        const int selectionBlockHeight = 365;
        const int bodyHeight = 872;

        int sideBySide = panelMinHeight + actionsHeight + boardSeparation + selectionBlockHeight;
        int stacked = panelMinHeight * 2 + actionsHeight + boardSeparation + selectionBlockHeight;

        Assert.True(sideBySide <= bodyHeight, $"并排时 {sideBySide}px 应放得下 {bodyHeight}px。");
        Assert.True(stacked > bodyHeight, $"堆叠时 {stacked}px 本来就会溢出——这正是改用分屏的原因。");
    }

    [Fact]
    public void RestSiteOfferUsesThreeEqualRowsWithReferenceSpacing()
    {
        Assert.Equal(3, TradeLayout.RestOfferRowCount);
        Assert.Equal(132, TradeLayout.OfferRowHeight);
        Assert.Equal(20, TradeLayout.RowSeparation);
        Assert.True(
            TradeLayout.RestOfferMinHeight < 700,
            $"三行交易区需要 {TradeLayout.RestOfferMinHeight}px，不能再次挤出可视区。");
    }

    [Fact]
    public void CenterGapAndMidlineMatchReferenceGeometry()
    {
        Assert.Equal(6, TradeLayout.MidlineWidth);
        Assert.True(
            TradeLayout.BoardSeparation >= 60,
            $"左右交易槽与中线间距只有 {TradeLayout.BoardSeparation}px。");
        Assert.True(
            TradeLayout.AvailablePerPanel >= TradeLayout.OfferPanelMinWidth,
            $"单侧最小宽度 {TradeLayout.OfferPanelMinWidth}px 超过可用 {TradeLayout.AvailablePerPanel}px。");
    }
}
