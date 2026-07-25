using System.Reflection;
using Godot;

namespace BetterMultiplayer.Trading;

internal static class TradeAssets
{
    private static Texture2D? _restTradeIcon;
    private static Texture2D? _goldTradeIcon;
    private static Texture2D? _assistSmithIcon;
    private static Texture2D? _roomLobbyIcon;
    private static Texture2D? _joinRoomIcon;
    private static Texture2D? _createRoomIcon;

    internal static Texture2D RestTradeIcon =>
        _restTradeIcon ??= LoadPng("rest-trade.png");

    internal static Texture2D GoldTradeIcon =>
        _goldTradeIcon ??= LoadPng("gold-trade.png");

    internal static Texture2D AssistSmithIcon =>
        _assistSmithIcon ??= LoadPng("assist-smith.png");

    internal static Texture2D RoomLobbyIcon =>
        _roomLobbyIcon ??= LoadPng("room-lobby.png");

    internal static Texture2D JoinRoomIcon =>
        _joinRoomIcon ??= LoadPng("join-room.png");

    internal static Texture2D CreateRoomIcon =>
        _createRoomIcon ??= LoadPng("create-room.png");

    internal static void WarmUp()
    {
        _ = RestTradeIcon;
        _ = GoldTradeIcon;
        _ = AssistSmithIcon;
        _ = RoomLobbyIcon;
        _ = JoinRoomIcon;
        _ = CreateRoomIcon;
    }

    private static Texture2D LoadPng(string fileName)
    {
        Assembly assembly = typeof(TradeAssets).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Trade icon resource not found: {fileName}");
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);

        Image image = new();
        Error error = image.LoadPngFromBuffer(buffer.ToArray());
        if (error != Error.Ok)
            throw new InvalidOperationException($"Could not read trade icon: {fileName}, {error}");
        return ImageTexture.CreateFromImage(image);
    }
}
