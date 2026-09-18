namespace BetterMultiplayer.Trading;

using BetterMultiplayer.UI;

/// <summary>
/// 交易界面的布局预算。
///
/// 0.6.0 出过一次「选择物品点不动」的 bug，根因就是没人算这个账：
/// 纵向要放两块 250px 报价面板 + 230px 选择网格 = 927px，而可用高度只有约 780px，
/// 网格被挤出可视区、点不到。
///
/// 现在改成 Together in Spire 式的左右分屏——用屏幕【宽度】而不是高度，
/// 高度上就宽裕了（board 250 + 按钮行 52 + 选择区约 365 ≈ 667，可用约 872）。
///
/// 下面这些数字是预算的单一来源。**改布局时必须重算，并由 TradeLayoutTests 兜住。**
/// 这里的每个常量都在生产代码里被真正使用——常量与代码脱节的话，测试就是假的。
/// </summary>
internal static class TradeLayout
{
    /// <summary>参考分辨率宽度（1080p）。</summary>
    internal const int ScreenWidth = 1920;

    /// <summary>overlay 外层安全区左右边距。</summary>
    internal const int SafeAreaMargin = UiFactory.OverlaySafeAreaMargin;

    /// <summary>overlay 内容区左右边距。</summary>
    internal const int ContentMargin = UiFactory.OverlayContentMargin;

    /// <summary>左右分屏的中线宽度，见 TradeOverlay.CreateMidline。</summary>
    internal const int MidlineWidth = 2;

    /// <summary>board 内元素之间的分隔，见 TradeOverlay.RenderActive。</summary>
    internal const int BoardSeparation = 16;

    /// <summary>报价面板里身份列（玩家名 + 锁定状态）的宽度。</summary>
    internal const int IdentityWidth = 165;

    /// <summary>报价面板左右内边距之和（各 18）。</summary>
    internal const int PanelPadding = 36;

    /// <summary>卡牌格宽度，见 TradeOverlay.CreateRestOffer。</summary>
    internal const int CardTileWidth = 145;

    /// <summary>卡牌格之间的分隔。</summary>
    internal const int CardTileSeparation = 10;

    /// <summary>遗物 / 药水那一列的宽度。</summary>
    internal const int ExtraColumnWidth = 160;

    /// <summary>一块报价面板所需的最小宽度。</summary>
    internal static int OfferPanelMinWidth =>
        IdentityWidth
        + BoardSeparation
        + CardTileWidth * 3 + CardTileSeparation * 2
        + BoardSeparation
        + ExtraColumnWidth
        + PanelPadding;

    /// <summary>左右分屏后每块面板实际能分到的宽度。</summary>
    internal static int AvailablePerPanel =>
        (ScreenWidth - SafeAreaMargin * 2 - ContentMargin * 2 - MidlineWidth - BoardSeparation * 2) / 2;
}
