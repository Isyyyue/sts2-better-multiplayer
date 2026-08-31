using System.Globalization;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using BetterMultiplayer.Trading;

namespace BetterMultiplayer.Diagnostics;

internal sealed record FeedbackParticipantContext(
    string PlayerId,
    string CharacterId,
    bool IsHost,
    bool IsLocal,
    bool IsConnected)
{
    internal FeedbackParticipantContext? Normalize()
    {
        string playerId = FeedbackValueSanitizer.Identifier(PlayerId);
        if (playerId == "unavailable")
            return null;

        return this with
        {
            PlayerId = playerId,
            CharacterId = FeedbackValueSanitizer.ModelId(CharacterId)
        };
    }
}

internal sealed record FeedbackCorrelationContext(
    string PlayerId,
    string LobbyId,
    string NetworkRole,
    string NetworkPlatform,
    int PlayerCount,
    DateTimeOffset ObservedAt,
    IReadOnlyList<FeedbackParticipantContext> Participants)
{
    internal FeedbackCorrelationContext(
        string playerId,
        string lobbyId,
        string networkRole,
        string networkPlatform,
        int playerCount,
        DateTimeOffset observedAt)
        : this(
            playerId,
            lobbyId,
            networkRole,
            networkPlatform,
            playerCount,
            observedAt,
            Array.Empty<FeedbackParticipantContext>())
    {
    }

    internal long Generation { get; init; }
    internal DateTimeOffset FirstObservedAt { get; init; }
    internal DateTimeOffset LastObservedAt { get; init; }
    internal string ScopedPlayerId => $"{NetworkPlatform}:{PlayerId}";
    internal string ScopedLobbyId => $"{NetworkPlatform}:{LobbyId}";

    internal FeedbackCorrelationContext? Normalize()
    {
        string platform = FeedbackValueSanitizer.Platform(NetworkPlatform);
        string role = FeedbackValueSanitizer.Role(NetworkRole);
        string playerId = FeedbackValueSanitizer.Identifier(PlayerId);
        string lobbyId = FeedbackValueSanitizer.Identifier(LobbyId);
        if (platform == "unknown" || role == "unknown" ||
            playerId == "unavailable" || lobbyId == "unavailable")
        {
            return null;
        }

        DateTimeOffset observedAt = ObservedAt.ToUniversalTime();
        DateTimeOffset firstObservedAt = FirstObservedAt == default
            ? observedAt
            : FirstObservedAt.ToUniversalTime();
        DateTimeOffset lastObservedAt = LastObservedAt == default
            ? observedAt
            : LastObservedAt.ToUniversalTime();
        if (lastObservedAt < firstObservedAt)
            (firstObservedAt, lastObservedAt) = (lastObservedAt, firstObservedAt);

        FeedbackParticipantContext[] participants = Participants
            .Select(participant => participant.Normalize())
            .Where(participant => participant is not null)
            .Cast<FeedbackParticipantContext>()
            .GroupBy(participant => participant.PlayerId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderByDescending(participant => participant.IsHost)
            .ThenByDescending(participant => participant.IsLocal)
            .ThenBy(participant => participant.PlayerId, StringComparer.Ordinal)
            .Take(4)
            .ToArray();

        return new FeedbackCorrelationContext(
            playerId,
            lobbyId,
            role,
            platform,
            Math.Clamp(Math.Max(PlayerCount, participants.Length), 0, 255),
            lastObservedAt,
            participants)
        {
            Generation = Math.Max(Generation, 0),
            FirstObservedAt = firstObservedAt,
            LastObservedAt = lastObservedAt
        };
    }
}

internal static class FeedbackContextTracker
{
    private static readonly object Gate = new();
    private static FeedbackCorrelationContext? _latest;
    private static long _generation;

    internal static void Observe(FeedbackCorrelationContext context)
    {
        FeedbackCorrelationContext? normalized = context.Normalize();
        if (normalized is null)
            return;

        lock (Gate)
        {
            bool sameLobby = _latest is not null &&
                _latest.ScopedLobbyId == normalized.ScopedLobbyId;
            long generation = sameLobby
                ? _latest!.Generation
                : ++_generation;
            DateTimeOffset firstObservedAt = sameLobby
                ? Min(_latest!.FirstObservedAt, normalized.FirstObservedAt)
                : normalized.FirstObservedAt;
            DateTimeOffset lastObservedAt = sameLobby
                ? Max(_latest!.LastObservedAt, normalized.LastObservedAt)
                : normalized.LastObservedAt;
            IReadOnlyList<FeedbackParticipantContext> participants =
                normalized.Participants.Count > 0
                    ? normalized.Participants
                    : sameLobby
                        ? _latest!.Participants
                        : Array.Empty<FeedbackParticipantContext>();

            _latest = normalized with
            {
                Generation = generation,
                FirstObservedAt = firstObservedAt,
                LastObservedAt = lastObservedAt,
                ObservedAt = lastObservedAt,
                PlayerCount = normalized.PlayerCount > 0
                    ? normalized.PlayerCount
                    : sameLobby
                        ? _latest!.PlayerCount
                        : 0,
                Participants = participants
            };
        }
    }

    internal static void ObserveSteamSession(
        ulong playerId,
        ulong lobbyId,
        string networkRole)
    {
        if (playerId == 0 || lobbyId == 0)
            return;

        Observe(new FeedbackCorrelationContext(
            playerId.ToString(CultureInfo.InvariantCulture),
            lobbyId.ToString(CultureInfo.InvariantCulture),
            networkRole,
            "steam",
            CurrentRunPlayerCount(),
            DateTimeOffset.UtcNow,
            CaptureParticipants(playerId, networkRole)));
    }

    internal static long CaptureCurrent()
    {
        try
        {
            RunManager manager = RunManager.Instance;
            INetGameService service = manager.NetService;
            if (!service.IsConnected ||
                service.Type is not (NetGameType.Host or NetGameType.Client))
            {
                return CurrentGeneration();
            }

            string? lobbyId = service.GetRawLobbyIdentifier();
            if (string.IsNullOrWhiteSpace(lobbyId) || service.NetId == 0)
                return CurrentGeneration();

            string role = service.Type == NetGameType.Host ? "host" : "client";
            Observe(new FeedbackCorrelationContext(
                service.NetId.ToString(CultureInfo.InvariantCulture),
                lobbyId,
                role,
                service.Platform.ToString(),
                CurrentRunPlayerCount(),
                DateTimeOffset.UtcNow,
                CaptureParticipants(service.NetId, role)));
        }
        catch
        {
            // Keep the last valid session after networking and the run tear down.
        }

        return CurrentGeneration();
    }

    internal static FeedbackCorrelationContext? Snapshot() =>
        Snapshot(DateTimeOffset.UtcNow);

    internal static FeedbackCorrelationContext? Snapshot(DateTimeOffset now)
    {
        lock (Gate)
        {
            if (_latest is null)
                return null;
            DateTimeOffset observedAt = _latest.LastObservedAt == default
                ? _latest.ObservedAt
                : _latest.LastObservedAt;
            return observedAt.ToUniversalTime() <
                now.ToUniversalTime() - DiagnosticRecorder.Retention
                    ? null
                    : _latest;
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _latest = null;
            _generation = 0;
        }
    }

    private static int CurrentRunPlayerCount()
    {
        try
        {
            return Math.Clamp(RunManager.Instance.State?.Players.Count ?? 0, 0, 255);
        }
        catch
        {
            return 0;
        }
    }

    private static IReadOnlyList<FeedbackParticipantContext> CaptureParticipants(
        ulong localPlayerId,
        string networkRole)
    {
        try
        {
            IReadOnlyList<Player>? players = RunManager.Instance.State?.Players;
            if (players is null)
                return Array.Empty<FeedbackParticipantContext>();

            ulong hostPlayerId = HostPlayerId(localPlayerId, networkRole);
            return players
                .Take(4)
                .Select(player => new FeedbackParticipantContext(
                    player.NetId.ToString(CultureInfo.InvariantCulture),
                    CharacterId(player),
                    player.NetId == hostPlayerId,
                    player.NetId == localPlayerId,
                    IsConnected(player.NetId, localPlayerId)))
                .ToArray();
        }
        catch
        {
            return Array.Empty<FeedbackParticipantContext>();
        }
    }

    private static ulong HostPlayerId(ulong localPlayerId, string networkRole)
    {
        if (networkRole == "host")
            return localPlayerId;
        try
        {
            return RunManager.Instance.NetService is NetClientGameService client
                ? client.HostNetId
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsConnected(ulong playerId, ulong localPlayerId)
    {
        if (playerId == localPlayerId)
            return true;
        try
        {
            INetGameService service = RunManager.Instance.NetService;
            if (!service.IsConnected)
                return false;
            if (service is NetHostGameService host)
                return GameApiCompatibility.IsHostPeerConnected(host, playerId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string CharacterId(Player player)
    {
        object? character = ReadMember(player, "Character") ??
            ReadMember(player, "CharacterModel");
        object? id = ReadMember(character, "Id") ??
            ReadMember(character, "ModelId") ??
            ReadMember(player, "CharacterId");
        return FeedbackValueSanitizer.ModelId(id?.ToString());
    }

    private static object? ReadMember(object? target, string name)
    {
        if (target is null)
            return null;
        try
        {
            const BindingFlags flags = BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic;
            return target.GetType().GetProperty(name, flags)?.GetValue(target) ??
                target.GetType().GetField(name, flags)?.GetValue(target);
        }
        catch
        {
            return null;
        }
    }

    private static long CurrentGeneration()
    {
        lock (Gate)
            return _latest?.Generation ?? 0;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;
}

internal static class FeedbackValueSanitizer
{
    internal static string Identifier(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length is 0 or > 96 ||
            !candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':'))
        {
            return "unavailable";
        }
        return candidate;
    }

    internal static string ModelId(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length is 0 or > 120 ||
            !candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':'))
        {
            return "unknown";
        }
        return candidate;
    }

    internal static string Role(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "host" => "host",
        "client" => "client",
        _ => "unknown"
    };

    internal static string Platform(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "steam" => "steam",
        _ => "unknown"
    };
}
