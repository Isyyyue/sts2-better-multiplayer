using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace BetterMultiplayer.Trading;

internal sealed class TradeRestSiteOption(Player owner) : CustomRestSiteOption(owner)
{
    private readonly Player _owner = owner;

    public override string OptionId => "BETTER_MULTIPLAYER_TRADE";

    public override string CustomIconPath => ImageHelper.GetImagePath("ui/rest_site/option_mend.png");

    public override bool IsEnabled => !TradeUsageTracker.HasUsed(_owner.NetId);

    public override async Task<bool> OnSelect()
    {
        Task<bool> result = TradeRestSiteFlow.WaitForResult(_owner.NetId);
        if (LocalContext.IsMe(_owner) && NRestSiteRoom.Instance is { } room)
            TradeOverlay.Show(room, TradeLocation.RestSite);

        await result;
        return false;
    }
}

[HarmonyPatch(typeof(RestSiteOption), nameof(RestSiteOption.Icon), MethodType.Getter)]
internal static class TradeRestSiteIconPatch
{
    [HarmonyPrefix]
    private static bool Prefix(RestSiteOption __instance, ref Texture2D __result)
    {
        switch (__instance)
        {
            case TradeRestSiteOption:
                __result = TradeAssets.RestTradeIcon;
                return false;
            case AssistSmithRestSiteOption:
                __result = TradeAssets.AssistSmithIcon;
                return false;
            default:
                return true;
        }
    }
}

[HarmonyPatch(typeof(RestSiteOption), nameof(RestSiteOption.Generate))]
internal static class TradeRestSiteOptionPatch
{
    [HarmonyPostfix]
    private static void Postfix(Player player, List<RestSiteOption> __result)
    {
        if (player.RunState.Players.Count > 1)
        {
            __result.Add(new TradeRestSiteOption(player));
            __result.Add(new AssistSmithRestSiteOption(player));
        }
    }
}

[HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.BeginRestSite))]
internal static class TradeRestSiteBeginPatch
{
    [HarmonyPrefix]
    private static void Prefix() => TradeCoordinator.BeginLocation(TradeLocation.RestSite);
}
