using System;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;
using BetterMultiplayer.Config;
using BetterMultiplayer.Trading.Messages;

namespace BetterMultiplayer.Trading;

/// <summary>
/// 惊喜模式：金币共享。
///
/// 开了之后全队共用一本账 —— 顶部那个金币数是"全队总和"，
/// 谁捡到钱总和就变大，谁花掉钱总和就变小。
/// 玩家看到的是"金币莫名其妙跟着队友走"，这正是这个模式想要的效果，
/// 所以设置页上不给任何说明（见 BetterMultiplayerConfig 里那段注释）。
///
/// 挂在 <see cref="Player.Gold"/> 的 setter 上，因为那是金币唯一的写入口：
/// 官方的 PlayerCmd.GainGold / LoseGold / SetGold，以及本 Mod 的交易结算
/// （TradeTransactionApplier 直接写 player.Gold），最后都走到这里。
///
/// 为什么不挂 PlayerCmd.GainGold：它是 async。Harmony 的 Postfix 打在方法入口
/// 返回的那个 Task 上，会在第一个 await 处就执行完，拿不到最终余额。
///
/// ★ 池子怎么维护（两步）：
///   1. 开局第一次同步时，把所有人的余额**加起来**当池子 —— 这就是
///      "一进去大家一样多"。必须求和，不能照抄某一个人的值：A=100、B=50 时
///      A 捡 10，照抄会让全员变 110（B 那 50 凭空消失），求和才是 160。
///   2. 之后每次变化，池子直接取**触发者的新值**。因为上一次同步已经把全员
///      设成了池子，"变化前的值"就等于池子，新值自然就是新池子。
///      （等价于"池子 += 增量"，但不需要 Prefix 去记旧值。）
///
/// ★ 权威只在房主。客户端不判断开关、不自己算，只被动接受房主推来的数。
///   这样即便两边设置不一致（比如客户端根本没开这个开关），金币也不会跑偏——
///   和 ADR-003「房主权威」是同一套思路。
/// </summary>
[HarmonyPatch(typeof(Player), "set_Gold")]
internal static class SharedGoldSync
{
    /// <summary>给别的玩家赋值时会再次进入 setter，用它挡住递归。</summary>
    private static bool _applying;

    /// <summary>全队共享的那个金币数。</summary>
    private static int _pool;

    /// <summary>池子是否已经用"全队总和"播种过。</summary>
    private static bool _seeded;

    /// <summary>池子绑在哪一局的 RunState 上——换局要重新播种。</summary>
    private static RunState? _boundState;

    /// <summary>等待网络加载结束的首次播种任务，换局或退回主菜单时取消。</summary>
    private static CancellationTokenSource _initializationLifetime = new();

    private static void Postfix(Player __instance)
    {
        // ★ 顺序很重要。原来这里把 TradeNetwork.IsHost 排在前面，
        //   而它自己就要去读 NetService —— 关卡加载早期那样读会抛异常。
        //   现在先做最便宜的判断，危险的访问全部挪进 ReadyState 里兜底。
        if (_applying || !BetterMultiplayerConfig.SurpriseSharedGold)
            return;

        try
        {
            RunState? state = ReadyState();
            if (state is null)
                return;

            // 换了一局就重新播种——不然第二局会接着用上一局的池子。
            if (!ReferenceEquals(_boundState, state))
            {
                _boundState = state;
                _seeded = false;
            }

            _pool = NextPool(_seeded, __instance.Gold, state);
            _seeded = true;

            Apply(state, _pool);
            Broadcast(_pool);
        }
        catch (Exception ex)
        {
            // ★ 绝不能让异常从 Player.Gold 的 setter 里冒出去。
            //   那是游戏自己的写入口，异常冒出去会打断调用方的流程——
            //   加载期就是"联机进去卡死"。同步失败只该影响这一处。
            BetterMultiplayerMod.Logger.Warn(
                $"Shared gold sync skipped this change: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// RunManager.Launch 发生在官方 NetLoadingHandle 仍然有效的窗口内。
    /// 延迟到网络加载结束后主动播种一次，覆盖“初始金币写入后没有新的 setter”场景。
    /// </summary>
    internal static void ScheduleInitialSync(RunState state)
    {
        if (!BetterMultiplayerConfig.SurpriseSharedGold)
            return;

        CancelPendingInitialization();
        CancellationToken token = _initializationLifetime.Token;
        TaskHelper.RunSafely(InitializeWhenReady(state, token));
    }

    /// <summary>清理退局时的延迟任务和本局池状态。</summary>
    internal static void Reset()
    {
        CancelPendingInitialization();
        _boundState = null;
        _seeded = false;
        _pool = 0;
    }

    /// <summary>
    /// 由延迟入口调用的生产初始化动作。它不依赖某个玩家再次写入 Gold，
    /// 因此首次池子一定来自完整 RunState 的总和。
    /// </summary>
    internal static bool InitializeInitialPool(RunState state)
    {
        if (!ReferenceEquals(_boundState, state))
        {
            _boundState = state;
            _seeded = false;
        }

        if (_seeded)
            return false;

        _pool = SeedPool(state);
        _seeded = true;
        Apply(state, _pool);
        Broadcast(_pool);
        return true;
    }

    private static async Task InitializeWhenReady(RunState state, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                RunManager manager = RunManager.Instance;
                if (!ReferenceEquals(manager.State, state))
                    return;

                if (ReadyState() is RunState readyState)
                {
                    InitializeInitialPool(readyState);
                    return;
                }

                if (!CanWaitForReadiness(manager, state))
                    return;

                NGame? game = NGame.Instance;
                if (game is null)
                    return;

                await game.AwaitProcessFrame(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Warn(
                $"Shared gold initial sync skipped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool CanWaitForReadiness(RunManager manager, RunState state)
    {
        try
        {
            if (!manager.IsInProgress || manager.NetService.Type != NetGameType.Host)
                return false;

            // Keep waiting while a host is the only participant. A multiplayer run
            // may finish loading before the next client is attached; leaving the
            // task alive lets the first later join still receive the initial pool.
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void CancelPendingInitialization()
    {
        _initializationLifetime.Cancel();
        _initializationLifetime.Dispose();
        _initializationLifetime = new CancellationTokenSource();
    }

    /// <summary>
    /// 把所有前置条件一次性确认完，不满足就返回 null。
    ///
    /// ★ 为什么要整体兜底：这里是被 Player.Gold 的 setter 调用的，
    /// 而金币在关卡加载、玩家初始化时就会被赋值——那一刻
    /// RunManager / NetService / State 都可能还没就绪。
    /// 任何一处裸访问抛出来都会卡住游戏加载。
    /// </summary>
    private static RunState? ReadyState()
    {
        try
        {
            RunManager manager = RunManager.Instance;
            RunState? state = manager.State;

            bool ready = ShouldSync(
                inProgress: manager.IsInProgress,
                connected: manager.NetService.IsConnected,
                gameLoading: manager.NetService.IsGameLoading,
                isHost: TradeNetwork.IsHost,
                playerCount: state?.Players.Count ?? 0);

            return ready ? state : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 是否该做同步。
    ///
    /// 抽成纯函数是为了让测试能直接守住这几个条件——"联机进去卡死"就是
    /// 这里少了一个条件造成的：关卡加载早期 Player.Gold 就会被赋值，
    /// 那一刻网络还没就绪，裸读 NetService 会抛异常。
    ///
    /// 现在五个条件缺一不可：
    ///   inProgress   —— 在真正的一局里（不是主菜单）
    ///   connected    —— 已经连上
    ///   !gameLoading —— ★ 关卡不在加载中。用户报的卡死正好发生在这个窗口
    ///   isHost       —— 权威只在房主
    ///   playerCount  —— 至少两个人，否则没什么可共享的
    /// </summary>
    internal static bool ShouldSync(
        bool inProgress,
        bool connected,
        bool gameLoading,
        bool isHost,
        int playerCount) =>
        inProgress && connected && !gameLoading && isHost && playerCount > 1;

    /// <summary>
    /// 开局播种：把所有人的余额加起来当池子。
    ///
    /// 这就是"一进去大家一样多"。注意是**求和**，不是照抄某一个人的值——
    /// 照抄会把队友的钱整个丢掉：A=100、B=50 时 A 捡 10，照抄会让全员变 110
    /// （B 那 50 凭空消失），求和才是 100+50+10 = 160。
    /// </summary>
    internal static int SeedPool(RunState state)
    {
        int total = 0;
        foreach (Player player in state.Players)
            total += player.Gold;

        return total;
    }

    /// <summary>
    /// 算下一次的池子值。
    ///
    /// 未播种时用"全队求和"——这是"一进去大家一样多"的来源；
    /// 播种之后直接用触发者的新值：上一次同步已经把全员设成了池子，
    /// 所以"变化前的值"就是池子，新值自然就是新池子。
    /// （等价于"池子 += 增量"，但不需要 Prefix 去记旧值。）
    /// </summary>
    internal static int NextPool(bool seeded, int triggeringGold, RunState state) =>
        seeded ? triggeringGold : SeedPool(state);

    /// <summary>客户端收到房主推来的统一余额后照抄。</summary>
    internal static void ApplyFromHost(int gold)
    {
        try
        {
            RunState? state = RunManager.Instance.State;
            if (state is not null)
                Apply(state, gold);
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Warn(
                $"Could not apply shared gold from host: {ex.GetType().Name}");
        }
    }

    private static void Apply(RunState state, int gold)
    {
        if (_applying)
            return;

        _applying = true;
        try
        {
            foreach (Player player in state.Players)
                player.Gold = gold;
        }
        finally
        {
            _applying = false;
        }
    }

    private static void Broadcast(int gold)
    {
        try
        {
            // 本地已经 Apply 过了，别让消息绕回来再放一次。
            TradeNetwork.Broadcast(new SharedGoldEvent { Gold = gold }, applyLocally: false);
        }
        catch (Exception ex)
        {
            // 广播失败只影响同步，不能影响本局的正常流程。
            BetterMultiplayerMod.Logger.Warn(
                $"Could not broadcast shared gold: {ex.GetType().Name}");
        }
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
internal static class SharedGoldRunLaunchPatch
{
    [HarmonyPostfix]
    private static void Postfix(RunState __result) =>
        SharedGoldSync.ScheduleInitialSync(__result);
}
