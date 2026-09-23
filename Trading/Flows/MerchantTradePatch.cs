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

namespace BetterMultiplayer.Trading.Flows;

[HarmonyPatch(typeof(NMerchantRoom), nameof(NMerchantRoom._Ready))]
internal static class MerchantTradePatch
{
    private static ulong? _buttonOwnerId;
    private static Action? _buttonCleanup;

    [HarmonyPostfix]
    private static void Postfix(NMerchantRoom __instance)
    {
        NetGameType netType = RunManager.Instance.NetService.Type;
        ulong ownerId = __instance.GetInstanceId();
        if (!TryBeginLifecycle(netType, ownerId))
            return;

        int playerCount = RunManager.Instance.State?.Players.Count ?? 0;
        BetterMultiplayerMod.Logger.Info(
            $"Initializing merchant gold trade button: players={playerCount}, net={netType}");
        __instance.TreeExiting += () =>
            TradeCoordinator.EndLocation(TradeLocation.Merchant, ownerId);
        DiagnosticRecorder.RecordMerchantRoom();
        Node uiParent = (Node?)NModalContainer.Instance ?? __instance;

        NButton input = CreateGoldTradeButton(
            () =>
            {
                DiagnosticRecorder.RecordTradeOverlayRequested(TradeLocation.Merchant);
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
            if (!CanUse(button) || !CanUse(input))
                return;
            string text = ModText.Get(TextKey.GoldTrade);
            string tooltip = ModText.Get(TextKey.GoldTradeTooltip);
            button.Text = text;
            button.TooltipText = tooltip;
            input.TooltipText = tooltip;
        };
        updateText();
        ModText.LanguageChanged += updateText;

        NMapScreen? map = NMapScreen.Instance;
        Callable hide = Callable.From(() =>
        {
            if (CanUse(input))
                input.Visible = false;
        });
        Callable show = Callable.From(() =>
        {
            if (CanUse(input))
                input.Visible = true;
        });
        map?.Connect(NMapScreen.SignalName.Opened, hide);
        map?.Connect(NMapScreen.SignalName.Closed, show);

        MerchantButtonCleanupState cleanupState = new();
        Action cleanup = () =>
        {
            if (!cleanupState.TryBeginCleanup())
                return;
            ModText.LanguageChanged -= updateText;
            if (map is not null && CanUse(map))
            {
                if (map.IsConnected(NMapScreen.SignalName.Opened, hide))
                    map.Disconnect(NMapScreen.SignalName.Opened, hide);
                if (map.IsConnected(NMapScreen.SignalName.Closed, show))
                    map.Disconnect(NMapScreen.SignalName.Closed, show);
            }
            QueueFreeSafely(input);
        };

        bool ownerChanged = ClaimButtonOwner(ownerId, cleanup);
        if (!ownerChanged)
            return;
        if (uiParent.GetNodeOrNull<Control>("BetterMultiplayerGoldTrade") is { } existing)
            QueueFreeSafely(existing);

        uiParent.AddChild(input);
        Callable.From(() =>
        {
            if (!CanUse(button) || !CanUse(input))
                return;
            DiagnosticRecorder.RecordMerchantButtonAdded(button, input);
        }).CallDeferred();
        __instance.TreeExiting += () =>
        {
            ReleaseButtonOwner(ownerId);
            cleanup();
        };
        BetterMultiplayerMod.Logger.Info(
            $"Merchant gold trade button added: parent={uiParent.Name}, visible={input.Visible}");
    }

    internal static bool TryBeginLifecycle(NetGameType netType, ulong ownerId)
    {
        if (!ShouldInitialize(netType))
            return false;

        TradeCoordinator.BeginLocation(TradeLocation.Merchant, ownerId);
        return true;
    }

    private static bool ShouldInitialize(NetGameType netType) =>
        netType is NetGameType.Host or NetGameType.Client;

    internal static bool ClaimButtonOwner(ulong ownerId, Action cleanup)
    {
        bool changed = _buttonOwnerId != ownerId;
        if (!changed)
        {
            cleanup();
            return false;
        }

        Action? previousCleanup = _buttonCleanup;
        _buttonOwnerId = ownerId;
        _buttonCleanup = cleanup;
        previousCleanup?.Invoke();
        return true;
    }

    internal static bool ReleaseButtonOwner(ulong ownerId)
    {
        if (_buttonOwnerId != ownerId)
            return false;
        _buttonOwnerId = null;
        _buttonCleanup = null;
        return true;
    }

    private static bool CanUse(Node? node) =>
        node is not null &&
        GodotObject.IsInstanceValid(node) &&
        !node.IsQueuedForDeletion();

    private static void QueueFreeSafely(Node? node)
    {
        if (node is null || !CanUse(node))
            return;

        Node? parent = node.GetParent();
        if (parent is not null && CanUse(parent))
            parent.RemoveChild(node);
        node.QueueFree();
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

internal sealed class MerchantButtonCleanupState
{
    private bool _cleanupStarted;

    internal bool TryBeginCleanup()
    {
        if (_cleanupStarted)
            return false;
        _cleanupStarted = true;
        return true;
    }
}
