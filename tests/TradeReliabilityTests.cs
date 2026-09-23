using System.Reflection;
using BetterMultiplayer.Config;

namespace BetterMultiplayer.Tests;

/// <summary>
/// 交易可靠性回归。这一组守的是玩家实际踩到的坑：
/// 「交易卡死，一直显示等待对方确定」和「交易报错之后遗物跟着没了」。
/// </summary>
[Collection("TradeState")]
public sealed class TradeReliabilityTests : IDisposable
{
    public TradeReliabilityTests() => ResetConfig();

    public void Dispose()
    {
        ResetConfig();
        TradeRestSiteFlow.Reset();
    }

    private static void ResetConfig() =>
        BetterMultiplayerConfig.AllowDuplicateRelicsAfterTrade = false;

    // ------------------------------------------------------------------
    // 设置页的「允许交易后重复遗物」开关
    // ------------------------------------------------------------------

    /// <summary>
    /// 默认必须是关着的。这个开关放行的是"交易后某一方会持有两个同名遗物"，
    /// 默认打开等于悄悄偏离原版规则。
    /// </summary>
    [Fact]
    public void DuplicateRelicsStayRejectedByDefault()
    {
        Assert.False(TradeValidator.DuplicateRelicsAllowed());
    }

    [Fact]
    public void TheSettingsSwitchAllowsDuplicateRelics()
    {
        BetterMultiplayerConfig.AllowDuplicateRelicsAfterTrade = true;

        Assert.True(TradeValidator.DuplicateRelicsAllowed());
    }

    [Fact]
    public void TurningTheSwitchBackOffRestoresTheOriginalRule()
    {
        BetterMultiplayerConfig.AllowDuplicateRelicsAfterTrade = true;
        Assert.True(TradeValidator.DuplicateRelicsAllowed());

        BetterMultiplayerConfig.AllowDuplicateRelicsAfterTrade = false;
        Assert.False(TradeValidator.DuplicateRelicsAllowed());
    }

    // ------------------------------------------------------------------
    // 休息处交易的等待
    // ------------------------------------------------------------------

    /// <summary>
    /// ★ 这条守的是"交易界面刚打开就被判定成结束"。
    ///
    /// Complete 只做 TrySetResult、不摘除注册表里的条目，所以在
    /// "上一次 Complete 刚跑完、上一次 WaitForResult 的 finally 还没执行"
    /// 的那个窗口里，注册表里留着一个【已完成】的 waiter。
    /// 复用它的话，新的等待会瞬间拿到上一次的结果——休息处那边看到的
    /// 就是选项在界面刚打开时就结束了（日志里 "Trade overlay shown"
    /// 紧跟 "chose rest site option ... success False" 正是这个现象）。
    /// </summary>
    [Fact]
    public void AnAlreadyCompletedWaitIsNotHandedToTheNextCaller()
    {
        TradeRestSiteFlow.Reset();
        InjectCompletedWaiter(playerId: 12);

        Task<bool> waiting = TradeRestSiteFlow.WaitForResult(12);

        Assert.False(waiting.IsCompleted);
    }

    /// <summary>一次结果只属于一次等待，不能漏给下一次。</summary>
    [Fact]
    public async Task AResultBelongsToTheWaitThatWasOpenWhenItArrived()
    {
        TradeRestSiteFlow.Reset();

        Task<bool> first = TradeRestSiteFlow.WaitForResult(12);
        Assert.False(first.IsCompleted);
        TradeRestSiteFlow.Complete(12, success: false);
        Assert.False(await first);

        Task<bool> second = TradeRestSiteFlow.WaitForResult(12);
        Assert.False(second.IsCompleted);
        TradeRestSiteFlow.Complete(12, success: true);
        Assert.True(await second);
    }

    /// <summary>等待开始之前到达的结果不该被补发——那属于上一次交易。</summary>
    [Fact]
    public void AResultArrivingBeforeTheWaitIsNotReplayed()
    {
        TradeRestSiteFlow.Reset();
        TradeRestSiteFlow.Complete(12, success: true);

        Task<bool> waiting = TradeRestSiteFlow.WaitForResult(12);

        Assert.False(waiting.IsCompleted);
    }

    /// <summary>换到新的篝火时，上一次留下的等待必须全部放行，不能挂着。</summary>
    [Fact]
    public async Task BeginningARestSiteReleasesEveryOutstandingWait()
    {
        TradeRestSiteFlow.Reset();
        Task<bool> waiting = TradeRestSiteFlow.WaitForResult(12);

        TradeRestSiteFlow.BeginRestSite();

        Assert.False(await waiting);
    }

    /// <summary>
    /// 手工往注册表里塞一个【已完成】的 waiter，
    /// 复现"Complete 刚跑完、旧 WaitForResult 还没清理"的那一瞬间。
    /// </summary>
    private static void InjectCompletedWaiter(ulong playerId)
    {
        FieldInfo field = typeof(TradeRestSiteFlow).GetField(
            "Waiters",
            BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new MissingFieldException(typeof(TradeRestSiteFlow).FullName, "Waiters");

        Dictionary<ulong, TaskCompletionSource<bool>> waiters =
            (Dictionary<ulong, TaskCompletionSource<bool>>)field.GetValue(null)!;

        TaskCompletionSource<bool> completed = new();
        completed.SetResult(true);
        waiters[playerId] = completed;
    }
}
