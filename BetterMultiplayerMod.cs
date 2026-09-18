using System;
using Godot;
using HarmonyLib;
using BaseLib.Config;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using BetterMultiplayer.Config;
using BetterMultiplayer.Trading;
using StsLogger = MegaCrit.Sts2.Core.Logging.Logger;

namespace BetterMultiplayer;

[ModInitializer(nameof(Initialize))]
public static class BetterMultiplayerMod
{
    public const string ModId = "BetterMultiplayer";
    public const string Version = "0.6.1";

    internal static StsLogger Logger { get; } = new(ModId, LogType.Generic);

    public static void Initialize()
    {
        Harmony harmony = new(ModId);
        harmony.PatchAll(typeof(BetterMultiplayerMod).Assembly);
        TradeAssets.WarmUp();

        RegisterModConfig();

        Logger.Info($"Better Multiplayer {Version} loaded");
        GD.Print($"[BetterMultiplayer] {Version} initialized");
    }

    /// <summary>
    /// 把设置页注册给 BaseLib。BaseLib 会在设置界面复制一行「Modding」作为入口。
    /// 注册失败不能影响 Mod 本体，所以这里吞掉异常只记日志——
    /// 设置页没了，交易功能仍然要能用。
    /// </summary>
    private static void RegisterModConfig()
    {
        try
        {
            ModConfigRegistry.Register(ModId, new BetterMultiplayerConfig());
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to register mod config: {ex}");
        }
    }
}

