using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using BetterMultiplayer.Trading;
using StsLogger = MegaCrit.Sts2.Core.Logging.Logger;

namespace BetterMultiplayer;

[ModInitializer(nameof(Initialize))]
public static class BetterMultiplayerMod
{
    public const string ModId = "BetterMultiplayer";
    public const string Version = "0.4.2";

    internal static StsLogger Logger { get; } = new(ModId, LogType.Generic);

    public static void Initialize()
    {
        Harmony harmony = new(ModId);
        harmony.PatchAll(typeof(BetterMultiplayerMod).Assembly);
        TradeAssets.WarmUp();

        Logger.Info($"Better Multiplayer {Version} loaded");
        GD.Print($"[BetterMultiplayer] {Version} initialized");
    }
}
