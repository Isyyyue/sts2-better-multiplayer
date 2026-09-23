namespace BetterMultiplayer.Trading.View;

using BetterMultiplayer.UI;

/// <summary>
/// 交易界面的布局预算。
///
/// 0.6.0 出过一次「选择物品点不动」的 bug，根因就是没人算这个账：
/// 纵向堆叠报价面板时，选择网格会被挤出可视区，玩家看得到物品却点不到。
///
/// 现在采用 Together in Spire 式左右分屏：每侧是玩家标题栏和三条等高交易槽，
/// 中央保留明确的分隔线。高度预算由三条槽的固定尺寸统一管理，宽度预算由
/// 参考 1920x1080 几何和 TradeLayoutTests 兜底。
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
    internal const int MidlineWidth = 6;

    /// <summary>两侧面板和中线之间的间距，见 TradeOverlay.RenderActive。</summary>
    internal const int BoardSeparation = 60;

    /// <summary>每侧玩家标题与交易行之间的间距。</summary>
    internal const int PanelSeparation = 12;

    /// <summary>三条交易行之间的间距，见 TradeOverlay.CreateRestOffer。</summary>
    internal const int RowSeparation = 20;

    /// <summary>玩家标题栏的最小高度。</summary>
    internal const int PlayerHeaderHeight = 54;

    /// <summary>卡牌、遗物、药水交易行的统一高度。</summary>
    internal const int OfferRowHeight = 132;

    /// <summary>报价行左侧标签列宽度。</summary>
    internal const int RowLabelWidth = 112;

    /// <summary>报价行标签和物品之间的间距。</summary>
    internal const int RowLabelSeparation = 12;

    /// <summary>报价面板左右内边距之和（各 12）。</summary>
    internal const int PanelPadding = 24;

    /// <summary>卡牌格宽度，见 TradeOverlay.CreateRestOffer。</summary>
    internal const int CardTileWidth = 142;

    /// <summary>卡牌格高度，和交易行高度共同保证点击区域完整可见。</summary>
    internal const int CardTileHeight = 106;

    /// <summary>卡牌格之间的分隔。</summary>
    internal const int CardTileSeparation = 8;

    /// <summary>遗物 / 药水槽的宽度。</summary>
    internal const int ExtraTileWidth = 220;

    /// <summary>遗物 / 药水槽的高度。</summary>
    internal const int ExtraTileHeight = 88;

    /// <summary>金币交易行的最小高度。</summary>
    internal const int GoldRowHeight = 132;

    /// <summary>金币图标宽度，见 TradeOverlay.CreateGoldOffer。</summary>
    internal const int GoldIconWidth = 124;

    /// <summary>金币图标高度，见 TradeOverlay.CreateGoldOffer。</summary>
    internal const int GoldIconHeight = 96;

    /// <summary>金币输入框宽度，见 TradeOverlay.CreateGoldOffer。</summary>
    internal const int GoldInputWidth = 260;

    /// <summary>一块左右分屏报价面板所需的最小宽度。</summary>
    internal static int OfferPanelMinWidth =>
        RowLabelWidth
        + RowLabelSeparation
        + CardTileWidth * 3 + CardTileSeparation * 2
        + PanelPadding;

    /// <summary>篝火交易面板固定显示的交易槽数量。</summary>
    internal const int RestOfferRowCount = 3;

    /// <summary>篝火交易面板的最小高度（标题栏 + 三条独立交易行）。</summary>
    internal static int RestOfferMinHeight =>
        PlayerHeaderHeight
        + PanelSeparation
        + OfferRowHeight * RestOfferRowCount
        + RowSeparation * 2;

    /// <summary>金币交易面板的最小高度（标题栏 + 一条金币交易行）。</summary>
    internal static int GoldOfferMinHeight =>
        PlayerHeaderHeight + PanelSeparation + GoldRowHeight;

    /// <summary>左右分屏后每块面板实际能分到的宽度。</summary>
    internal static int AvailablePerPanel =>
        (ScreenWidth - SafeAreaMargin * 2 - ContentMargin * 2 - MidlineWidth - BoardSeparation * 2) / 2;
}
