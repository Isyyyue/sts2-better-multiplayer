using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.addons.mega_text;
using BetterMultiplayer.Localization;
using BetterMultiplayer.Trading;

namespace BetterMultiplayer.Lobby;

[HarmonyPatch(typeof(NMultiplayerSubmenu), nameof(NMultiplayerSubmenu._Ready))]
internal static class MultiplayerMenuPatch
{
    [HarmonyPostfix]
    private static void Postfix(NMultiplayerSubmenu __instance)
    {
        Control container = __instance.GetNode<Control>("ButtonContainer");
        if (container.GetNodeOrNull<NSubmenuButton>("BetterMultiplayerButton") is not null)
            return;

        NSubmenuButton joinButton = Traverse.Create(__instance)
            .Field("_joinButton")
            .GetValue<NSubmenuButton>();
        NSubmenuButton roomButton = (NSubmenuButton)joinButton.Duplicate(14);
        roomButton.Name = "BetterMultiplayerButton";
        container.AddChild(roomButton);
        container.MoveChild(roomButton, joinButton.GetIndex());

        MegaLabel title = roomButton.GetNode<MegaLabel>("%Title");
        MegaRichTextLabel description = roomButton.GetNode<MegaRichTextLabel>("%Description");
        Action updateText = () =>
        {
            if (!GodotObject.IsInstanceValid(roomButton))
                return;
            title.SetTextAutoSize(ModText.Get(TextKey.RoomMultiplayer));
            description.Text = ModText.Get(TextKey.RoomMultiplayerDescription);
        };
        updateText();
        ModText.LanguageChanged += updateText;
        roomButton.TreeExiting += () => ModText.LanguageChanged -= updateText;
        roomButton.GetNode<TextureRect>("Icon").Texture = TradeAssets.RoomLobbyIcon;
        SetBlueGrayBackground(roomButton);
        roomButton.Connect(
            NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => LobbyMenu.Show(__instance)));
    }

    private static void SetBlueGrayBackground(NSubmenuButton button)
    {
        Control panel = button.GetNode<Control>("BgPanel");
        if (panel.Material is not ShaderMaterial source)
            return;

        ShaderMaterial material = (ShaderMaterial)source.Duplicate();
        panel.Material = material;
        material.SetShaderParameter("h", 0.58f);
        material.SetShaderParameter("s", 0.45f);
        material.SetShaderParameter("v", 0.82f);

        Traverse buttonFields = Traverse.Create(button);
        buttonFields.Field("_hsv").SetValue(material);
        buttonFields.Field("_defaultV").SetValue(0.82f);
    }
}

[HarmonyPatch(typeof(NSubmenu), nameof(NSubmenu.OnSubmenuClosed))]
internal static class MultiplayerHostSubmenuClosedPatch
{
    [HarmonyPostfix]
    private static void Postfix(NSubmenu __instance)
    {
        if (__instance is NMultiplayerHostSubmenu)
            RoomSession.CancelPending();
    }
}
