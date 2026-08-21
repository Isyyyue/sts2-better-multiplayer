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

        NButton input = CreateGoldTradeButton(
            () =>
            {
                DiagnosticRecorder.RecordTradeOverlayRequested();
                TradeOverlay.Show(__instance, TradeLocation.Merchant);
            },
            out Button button);
        input.Name = "BetterMultiplayerGoldTrade";
        input.AnchorLeft = 1f;
        input.AnchorRight = 1f;
        input.AnchorTop = 0f;
        input.AnchorBottom = 0f;
        input.OffsetLeft = -266;
        input.OffsetRight = -36;
        input.OffsetTop = 210;
        input.OffsetBottom = 274;
        input.ZIndex = 3000;

        Action updateText = () =>
        {
            if (!GodotObject.IsInstanceValid(button) || !GodotObject.IsInstanceValid(input))
                return;
            string text = ModText.Get(TextKey.GoldTrade);
            string tooltip = ModText.Get(TextKey.GoldTradeTooltip);
            button.Text = text;
            button.TooltipText = tooltip;
            input.TooltipText = tooltip;
        };
        updateText();
        ModText.LanguageChanged += updateText;
        uiParent.AddChild(input);
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(button) || !GodotObject.IsInstanceValid(input))
                return;
            DiagnosticRecorder.RecordMerchantButtonAdded(button, input);
        }).CallDeferred();

        NMapScreen? map = NMapScreen.Instance;
        Callable hide = Callable.From(() => input.Visible = false);
        Callable show = Callable.From(() => input.Visible = true);
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
            if (GodotObject.IsInstanceValid(input))
                input.QueueFree();
        };
        BetterMultiplayerMod.Logger.Info(
            $"Merchant gold trade button added: parent={uiParent.Name}, visible={input.Visible}");
    }

    private static NButton CreateGoldTradeButton(Action onReleased, out Button visual)
    {
        StyleBoxFlat normalStyle = UiFactory.PanelStyle(UiFactory.Accent, UiFactory.Border, 1, 5);
        StyleBoxFlat hoverStyle = UiFactory.PanelStyle(
            UiFactory.Accent.Lightened(0.12f),
            UiFactory.Accent,
            1,
            5);
        StyleBoxFlat pressedStyle = UiFactory.PanelStyle(
            UiFactory.Accent.Darkened(0.1f),
            UiFactory.Accent,
            2,
            5);

        Button button = new()
        {
            Name = "GoldTradeButton",
            CustomMinimumSize = new Vector2(230, 64),
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Icon = TradeAssets.GoldTradeIcon,
            ExpandIcon = true
        };
        button.AddThemeStyleboxOverride("normal", normalStyle);
        button.AddThemeStyleboxOverride("hover", hoverStyle);
        button.AddThemeStyleboxOverride("pressed", pressedStyle);
        button.AddThemeColorOverride("font_color", Colors.Black);
        button.AddThemeFontSizeOverride("font_size", 20);

        NButton input = new()
        {
            CustomMinimumSize = new Vector2(230, 64),
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        input.AddChild(button);
        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        input.Focused += _ =>
        {
            if (!GodotObject.IsInstanceValid(button))
                return;
            DiagnosticRecorder.RecordControl("focused", "merchant_gold_trade", button, input);
            button.AddThemeStyleboxOverride("normal", hoverStyle);
        };
        input.Unfocused += _ =>
        {
            if (!GodotObject.IsInstanceValid(button))
                return;
            DiagnosticRecorder.RecordControl("unfocused", "merchant_gold_trade", button, input);
            button.AddThemeStyleboxOverride("normal", normalStyle);
        };
        input.MousePressed += _ =>
        {
            if (!GodotObject.IsInstanceValid(button))
                return;
            DiagnosticRecorder.RecordControl("mouse_pressed", "merchant_gold_trade", button, input);
            button.AddThemeStyleboxOverride("normal", pressedStyle);
        };
        input.MouseReleased += _ =>
        {
            if (!GodotObject.IsInstanceValid(button))
                return;
            DiagnosticRecorder.RecordControl("mouse_released", "merchant_gold_trade", button, input);
            button.AddThemeStyleboxOverride("normal", hoverStyle);
        };
        input.Released += _ =>
        {
            if (!GodotObject.IsInstanceValid(button))
                return;
            DiagnosticRecorder.RecordControl("released", "merchant_gold_trade", button, input);
            BetterMultiplayerMod.Logger.Info(
                $"Native merchant button released: text=\"{button.Text}\", name={button.Name}");
            onReleased();
        };

        visual = button;
        return input;
    }
}
