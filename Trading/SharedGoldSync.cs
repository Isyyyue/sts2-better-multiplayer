using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using BetterMultiplayer.Config;
using BetterMultiplayer.Trading.Messages;

namespace BetterMultiplayer.Trading;

/// <summary>
/// 惊喜模式：金币共享。
///
/// 开了之后全队持有同一个金币数——谁捡到钱大家一起变多，谁花掉钱大家一起变少。
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
/// ★ 权威只在房主。客户端不判断开关、不自己算，只被动接受房主推来的数。
///   这样即便两边设置不一致（比如客户端根本没开这个开关），金币也不会跑偏——
///   和 ADR-003「房主权威」是同一套思路。
/// </summary>
[HarmonyPatch(typeof(Player), "set_Gold")]
internal static class SharedGoldSync
{
    /// <summary>给别的玩家赋值时会再次进入 setter，用它挡住递归。</summary>
    private static bool _applying;

    private static void Postfix(Player __instance)
    {
        if (_applying || !BetterMultiplayerConfig.SurpriseSharedGold || !TradeNetwork.IsHost)
            return;

        // 单人跑就一个玩家，没什么可共享的。
        RunState? state = RunManager.Instance.State;
        if (state is null || state.Players.Count <= 1)
            return;

        int shared = __instance.Gold;
        Apply(shared);
        Broadcast(shared);
    }

    /// <summary>客户端收到房主推来的统一余额后照抄。</summary>
    internal static void ApplyFromHost(int gold) => Apply(gold);

    private static void Apply(int gold)
    {
        if (_applying)
            return;

        RunState? state = RunManager.Instance.State;
        if (state is null)
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
