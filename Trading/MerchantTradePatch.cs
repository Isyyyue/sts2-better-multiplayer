using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using BetterMultiplayer.Localization;
using BetterMultiplayer.UI;
using BetterMultiplayer.Diagnostics;

namespace BetterMultiplayer.Trading;

[HarmonyPatch(typeof(NMerchantRoom), nameof(NMerchantRoom._Ready))]
internal static class MerchantTradePatch
{
    [HarmonyPostfix]
    private static void Postfix(NMerchantRoom __instance)
    {
        int playerCount = RunManager.Instance.State?.Players.Count ?? 0;
        NetGameType netType = RunManager.Instance.NetService.Type;
        BetterMultiplayerMod.Logger.Info(
            $"Initializing merchant gold trade button: players={playerCount}, net={netType}");
        if (playerCount <= 1)
            return;

        DiagnosticRecorder.RecordMerchantRoom();
        TradeCoordinator.BeginLocation(TradeLocation.Merchant);
        Node uiParent = (Node?)NModalContainer.Instance ?? __instance;
        if (uiParent.GetNodeOrNull<Control>("BetterMultiplayerGoldTrade") is not null)
            return;

        HBoxContainer toolbar = new()
        {
            Name = "BetterMultiplayerGoldTrade",
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = -286,
            OffsetRight = -36,
            OffsetTop = 210,
            OffsetBottom = 272,
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Pass,
            ZIndex = 3000
        };
        Button button = UiFactory.Button(
            ModText.Get(TextKey.GoldTrade),
            () =>
            {
                DiagnosticRecorder.RecordTradeOverlayRequested();
                TradeOverlay.Show(__instance, TradeLocation.Merchant);
            },
            primary: true,
            diagnosticId: "merchant_gold_trade");
        button.Name = "GoldTradeButton";
        button.CustomMinimumSize = new Vector2(230, 64);
        button.AddThemeFontSizeOverride("font_size", 20);
        button.Icon = TradeAssets.GoldTradeIcon;
        button.ExpandIcon = true;
        Action updateText = () =>
        {
            if (!GodotObject.IsInstanceValid(button))
                return;
            button.Text = ModText.Get(TextKey.GoldTrade);
            button.TooltipText = ModText.Get(TextKey.GoldTradeTooltip);
        };
        updateText();
        ModText.LanguageChanged += updateText;
        toolbar.AddChild(button);
        uiParent.AddChild(toolbar);
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(button))
                return;
            if (button.GetNodeOrNull<NButton>("BetterMultiplayerNativeInput") is { } input)
                DiagnosticRecorder.RecordMerchantButtonAdded(button, input);
        }).CallDeferred();

        NMapScreen? map = NMapScreen.Instance;
        Callable hide = Callable.From(() => toolbar.Visible = false);
        Callable show = Callable.From(() => toolbar.Visible = true);
        map?.Connect(NMapScreen.SignalName.Opened, hide);
        map?.Connect(NMapScreen.SignalName.Closed, show);
        __instance.TreeExiting += () =>
        {
            ModText.LanguageChanged -= updateText;
            if (map is not null && GodotObject.IsInstanceValid(map))
            {
                if (map.IsConnected(NMapScreen.SignalName.Opened, hide))
                    map.Disconnect(NMapScreen.SignalName.Opened, hide);
                if (map.IsConnected(NMapScreen.SignalName.Closed, show))
                    map.Disconnect(NMapScreen.SignalName.Closed, show);
            }
            if (GodotObject.IsInstanceValid(toolbar))
                toolbar.QueueFree();
        };
        BetterMultiplayerMod.Logger.Info(
            $"Merchant gold trade button added: parent={uiParent.Name}, visible={toolbar.Visible}");
    }
}
