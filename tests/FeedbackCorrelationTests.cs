using System.Reflection;
using System.Text.Json;
using BetterMultiplayer.Diagnostics;
using BetterMultiplayer.Lobby;
using BetterMultiplayer.Trading;

namespace BetterMultiplayer.Tests;

[Collection("TradeState")]
public sealed class FeedbackCorrelationTests : IDisposable
{
    private const string PlayerId = "76561198012345678";
    private const string OtherPlayerId = "76561198087654321";
    private const string LobbyId = "109775241012345678";
    private static readonly DateTimeOffset FirstReportAt =
        DateTimeOffset.Parse("2026-08-25T08:00:00Z");

    public void Dispose()
    {
        Type? tracker = ProductionAssembly.GetType(
            "BetterMultiplayer.Diagnostics.FeedbackContextTracker");
        tracker?.GetMethod("ResetForTests", StaticNonPublic)?.Invoke(null, null);
        RoomSession.Clear();
    }

    [Fact]
    public void ReportsFromSamePlayerAndLobbyCorrelateWithoutReplyMetadata()
    {
        object firstContext = CreateContext(PlayerId, LobbyId, FirstReportAt.AddMinutes(-2));
        object secondContext = CreateContext(PlayerId, LobbyId, FirstReportAt.AddMinutes(-2));
        object otherPlayerContext = CreateContext(
            OtherPlayerId,
            LobbyId,
            FirstReportAt.AddMinutes(-1));

        using JsonDocument first = CreatePayload(
            firstContext,
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            FirstReportAt);
        using JsonDocument second = CreatePayload(
            secondContext,
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            FirstReportAt.AddMinutes(1));
        using JsonDocument otherPlayer = CreatePayload(
            otherPlayerContext,
            Guid.Parse("33333333-3333-4333-8333-333333333333"),
            FirstReportAt.AddMinutes(2));

        Assert.Equal(UserId(first), UserId(second));
        Assert.NotEqual(UserId(first), UserId(otherPlayer));
        Assert.Equal(LobbyTag(first), LobbyTag(second));
        Assert.Equal(LobbyTag(first), LobbyTag(otherPlayer));
        Assert.NotEqual(EventId(first), EventId(second));
        Assert.NotEqual(Timestamp(first), Timestamp(second));
        Assert.Equal($"steam:{PlayerId}", UserId(first));
        Assert.Equal($"steam:{LobbyId}", LobbyTag(first));
        Assert.False(first.RootElement.GetProperty("user").TryGetProperty("ip_address", out _));
        Assert.False(first.RootElement.GetProperty("user").TryGetProperty("email", out _));
        Assert.Equal(
            Timestamp(first),
            first.RootElement.GetProperty("extra")
                .GetProperty("diagnostics")
                .GetProperty("submitted_at_utc")
                .GetString());
    }

    [Fact]
    public void LastMultiplayerContextSurvivesLobbyCleanupUntilFeedbackCreation()
    {
        Type tracker = Required(ProductionAssembly.GetType(
            "BetterMultiplayer.Diagnostics.FeedbackContextTracker"));
        MethodInfo observe = Required(tracker.GetMethod("Observe", StaticNonPublic));
        MethodInfo snapshot = Required(tracker.GetMethods(StaticNonPublic)
            .SingleOrDefault(candidate =>
                candidate.Name == "Snapshot" &&
                candidate.GetParameters().Length == 0));

        object context = CreateContext(PlayerId, LobbyId, FirstReportAt.AddMinutes(-2));
        observe.Invoke(null, [context]);
        RoomSession.Clear();

        object retained = Assert.IsAssignableFrom<object>(snapshot.Invoke(null, null));
        using JsonDocument payload = CreatePayload(
            retained,
            Guid.Parse("44444444-4444-4444-8444-444444444444"),
            FirstReportAt);

        Assert.Equal($"steam:{PlayerId}", UserId(payload));
        Assert.Equal($"steam:{LobbyId}", LobbyTag(payload));
    }

    [Fact]
    public void ThreePlayerReportIdentifiesEveryCharacterAndTheReportingClient()
    {
        object context = CreateContextWithParticipants(
            ("101", "sts2:ironclad", true, false),
            ("202", "examplemod:alchemist", false, true),
            ("303", "sts2:silent", false, false));

        using JsonDocument payload = CreatePayload(
            context,
            Guid.Parse("55555555-5555-4555-8555-555555555555"),
            FirstReportAt);

        JsonElement multiplayer = payload.RootElement.GetProperty("extra")
            .GetProperty("diagnostics")
            .GetProperty("multiplayer");
        JsonElement participants = multiplayer.GetProperty("participants");
        Assert.Equal("reporter_local_only", multiplayer.GetProperty("environment_scope").GetString());
        Assert.Equal(3, participants.GetArrayLength());
        Assert.Contains(participants.EnumerateArray(), participant =>
            participant.GetProperty("player_id").GetString() == "steam:101" &&
            participant.GetProperty("character_id").GetString() == "sts2:ironclad" &&
            participant.GetProperty("is_host").GetBoolean());
        Assert.Contains(participants.EnumerateArray(), participant =>
            participant.GetProperty("player_id").GetString() == "steam:202" &&
            participant.GetProperty("character_id").GetString() == "examplemod:alchemist" &&
            participant.GetProperty("is_local").GetBoolean());
        Assert.Contains(participants.EnumerateArray(), participant =>
            participant.GetProperty("player_id").GetString() == "steam:303" &&
            participant.GetProperty("character_id").GetString() == "sts2:silent");
    }

    [Fact]
    public void ConcurrentTradePairsRemainSeparatedBySession()
    {
        MethodInfo record = Required(typeof(DiagnosticRecorder)
            .GetMethods(StaticNonPublic)
            .SingleOrDefault(candidate =>
                candidate.Name == "RecordSessionSnapshot" &&
                candidate.GetParameters().Length == 2));
        DiagnosticRecorder.ResetForTests();
        record.Invoke(null, [Session(11, 101, 202, TradeLocation.Merchant), "active"]);
        record.Invoke(null, [Session(22, 303, 404, TradeLocation.RestSite), "committing"]);

        DateTimeOffset createdAt = DiagnosticRecorder.Snapshot()[^1].Timestamp.AddTicks(1);
        using JsonDocument payload = CreatePayload(
            CreateContextWithParticipants(
                ("101", "sts2:ironclad", true, true),
                ("202", "sts2:silent", false, false),
                ("303", "sts2:necrobinder", false, false),
                ("404", "mod:visitor", false, false)),
            Guid.Parse("66666666-6666-4666-8666-666666666666"),
            createdAt);

        JsonElement events = payload.RootElement.GetProperty("extra")
            .GetProperty("diagnostics")
            .GetProperty("events");
        Assert.Contains(events.EnumerateArray(), entry => PairMatches(entry, "11", "101", "202"));
        Assert.Contains(events.EnumerateArray(), entry => PairMatches(entry, "22", "303", "404"));
    }

    [Fact]
    public void AssistSmithReportIncludesOwnerTargetCardAndOutcome()
    {
        MethodInfo record = Required(typeof(DiagnosticRecorder)
            .GetMethods(StaticNonPublic)
            .SingleOrDefault(candidate =>
                candidate.Name == "RecordAssistSmith" &&
                candidate.GetParameters().Length == 8));
        DiagnosticRecorder.ResetForTests();
        record.Invoke(null, [
            "result_received",
            (ulong)202,
            (ulong)303,
            7,
            "examplemod:focused-strike",
            1,
            true,
            "completed"]);

        DateTimeOffset createdAt = DiagnosticRecorder.Snapshot()[^1].Timestamp.AddTicks(1);
        using JsonDocument payload = CreatePayload(
            CreateContextWithParticipants(
                ("101", "sts2:ironclad", true, false),
                ("202", "sts2:silent", false, true),
                ("303", "examplemod:alchemist", false, false)),
            Guid.Parse("77777777-7777-4777-8777-777777777777"),
            createdAt);

        JsonElement facts = payload.RootElement.GetProperty("extra")
            .GetProperty("diagnostics")
            .GetProperty("events")[0]
            .GetProperty("facts");
        Assert.Equal("202", facts.GetProperty("actor_id").GetString());
        Assert.Equal("303", facts.GetProperty("target_id").GetString());
        Assert.Equal(7, facts.GetProperty("card_index").GetInt32());
        Assert.Equal("examplemod:focused-strike", facts.GetProperty("item_id").GetString());
        Assert.Equal("result_received", facts.GetProperty("stage").GetString());
        Assert.True(facts.GetProperty("success").GetBoolean());
        Assert.Equal("completed", facts.GetProperty("reason").GetString());
    }

    [Fact]
    public void FeedbackIncludesOnlyDiagnosticsFromTheCurrentLobbyGeneration()
    {
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        Type tracker = Required(ProductionAssembly.GetType(
            "BetterMultiplayer.Diagnostics.FeedbackContextTracker"));
        MethodInfo observe = Required(tracker.GetMethod("Observe", StaticNonPublic));
        MethodInfo snapshot = Required(tracker.GetMethods(StaticNonPublic)
            .SingleOrDefault(candidate =>
                candidate.Name == "Snapshot" &&
                candidate.GetParameters().Length == 0));

        observe.Invoke(null, [CreateContext(PlayerId, "10001", createdAt.AddMinutes(-2))]);
        object firstLobby = Required(snapshot.Invoke(null, null));
        long firstGeneration = ContextGeneration(firstLobby);

        observe.Invoke(null, [CreateContext(PlayerId, "20002", createdAt.AddMinutes(-1))]);
        object secondLobby = Required(snapshot.Invoke(null, null));
        long secondGeneration = ContextGeneration(secondLobby);

        Assert.NotEqual(firstGeneration, secondGeneration);
        DiagnosticEntry[] entries =
        [
            new DiagnosticEntry(
                1,
                createdAt.AddSeconds(-20),
                DiagnosticEventCode.MerchantRoomReady,
                DiagnosticControlId.None,
                null,
                ContextGeneration: firstGeneration),
            new DiagnosticEntry(
                2,
                createdAt.AddSeconds(-10),
                DiagnosticEventCode.AssistSmithChanged,
                DiagnosticControlId.None,
                null,
                new DiagnosticTradeFacts(Stage: "selected", ActorId: PlayerId),
                secondGeneration)
        ];

        using JsonDocument payload = CreatePayload(
            secondLobby,
            Guid.Parse("88888888-8888-4888-8888-888888888888"),
            createdAt,
            entries);

        JsonElement events = payload.RootElement.GetProperty("extra")
            .GetProperty("diagnostics")
            .GetProperty("events");
        JsonElement entry = Assert.Single(events.EnumerateArray());
        Assert.Equal(2, entry.GetProperty("sequence").GetInt64());
        Assert.Equal("assist_smith.changed", entry.GetProperty("code").GetString());
        Assert.Equal("steam:20002", LobbyTag(payload));
    }

    [Fact]
    public void MultiplayerContextExpiresImmediatelyAfterThreeDays()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        Type tracker = Required(ProductionAssembly.GetType(
            "BetterMultiplayer.Diagnostics.FeedbackContextTracker"));
        MethodInfo observe = Required(tracker.GetMethod("Observe", StaticNonPublic));
        MethodInfo snapshotAt = Required(tracker.GetMethods(StaticNonPublic)
            .SingleOrDefault(candidate =>
                candidate.Name == "Snapshot" &&
                candidate.GetParameters().Length == 1));

        observe.Invoke(null, [CreateContext(PlayerId, LobbyId, observedAt)]);

        Assert.NotNull(snapshotAt.Invoke(null, [observedAt + DiagnosticRecorder.Retention]));
        Assert.Null(snapshotAt.Invoke(
            null,
            [observedAt + DiagnosticRecorder.Retention + TimeSpan.FromTicks(1)]));
    }

    [Fact]
    public void CorrelatedPayloadKeepsAuthorizedIdsButRejectsSensitiveFreeText()
    {
        object context = CreateContextWithParticipants(
            ("101", "C:\\Users\\Private\\Mods room-password-secret", true, false),
            ("202", "examplemod:alchemist", false, true));

        using JsonDocument payload = CreatePayload(
            context,
            Guid.Parse("99999999-9999-4999-8999-999999999999"),
            FirstReportAt);
        string json = payload.RootElement.GetRawText();

        Assert.Contains("steam:202", json, StringComparison.Ordinal);
        Assert.Contains($"steam:{LobbyId}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\\\Users", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Private", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ip_address", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("room-password-secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) <= FeedbackEventFactory.MaxEventBytes);
    }

    private static Assembly ProductionAssembly => typeof(FeedbackEventFactory).Assembly;

    private static BindingFlags StaticNonPublic =>
        BindingFlags.Static | BindingFlags.NonPublic;

    private static BindingFlags InstancePublicNonPublic =>
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static object CreateContext(
        string playerId,
        string lobbyId,
        DateTimeOffset observedAt)
    {
        Type context = Required(ProductionAssembly.GetType(
            "BetterMultiplayer.Diagnostics.FeedbackCorrelationContext"));
        ConstructorInfo constructor = Required(context
            .GetConstructors(InstancePublicNonPublic)
            .SingleOrDefault(candidate => candidate.GetParameters().Length == 6));
        return Assert.IsAssignableFrom<object>(constructor.Invoke([
            playerId,
            lobbyId,
            "host",
            "steam",
            4,
            observedAt]));
    }

    private static object CreateContextWithParticipants(
        params (string PlayerId, string CharacterId, bool IsHost, bool IsLocal)[] values)
    {
        Type contextType = Required(ProductionAssembly.GetType(
            "BetterMultiplayer.Diagnostics.FeedbackCorrelationContext"));
        Type participantType = Required(ProductionAssembly.GetType(
            "BetterMultiplayer.Diagnostics.FeedbackParticipantContext"));
        Array participants = Array.CreateInstance(participantType, values.Length);
        for (int index = 0; index < values.Length; index++)
        {
            (string playerId, string characterId, bool isHost, bool isLocal) = values[index];
            participants.SetValue(Activator.CreateInstance(
                participantType,
                playerId,
                characterId,
                isHost,
                isLocal,
                true), index);
        }

        ConstructorInfo constructor = Required(contextType
            .GetConstructors(InstancePublicNonPublic)
            .SingleOrDefault(candidate => candidate.GetParameters().Length == 7));
        return Assert.IsAssignableFrom<object>(constructor.Invoke([
            "202",
            LobbyId,
            "client",
            "steam",
            values.Length,
            FirstReportAt.AddMinutes(-2),
            participants]));
    }

    private static JsonDocument CreatePayload(
        object context,
        Guid eventId,
        DateTimeOffset createdAt,
        IReadOnlyList<DiagnosticEntry>? entries = null)
    {
        MethodInfo create = Assert.Single(
            typeof(FeedbackEventFactory).GetMethods(StaticNonPublic),
            candidate =>
                candidate.Name == nameof(FeedbackEventFactory.Create) &&
                candidate.GetParameters().Length == 5);
        FeedbackEventPayload payload = Assert.IsType<FeedbackEventPayload>(create.Invoke(
            null,
            [entries ?? DiagnosticRecorder.Snapshot(createdAt), SafeSystem(), eventId, createdAt, context]));
        return JsonDocument.Parse(payload.EventBytes);
    }

    private static long ContextGeneration(object context)
    {
        PropertyInfo property = Required(context.GetType().GetProperty(
            "Generation",
            InstancePublicNonPublic));
        return Assert.IsType<long>(property.GetValue(context));
    }

    private static string UserId(JsonDocument document) =>
        document.RootElement.GetProperty("user").GetProperty("id").GetString()!;

    private static string LobbyTag(JsonDocument document) =>
        document.RootElement.GetProperty("tags")
            .GetProperty("multiplayer.lobby_id")
            .GetString()!;

    private static string EventId(JsonDocument document) =>
        document.RootElement.GetProperty("event_id").GetString()!;

    private static string Timestamp(JsonDocument document) =>
        document.RootElement.GetProperty("timestamp").GetString()!;

    private static TradeSessionSnapshot Session(
        ulong sessionId,
        ulong playerA,
        ulong playerB,
        TradeLocation location) => new()
    {
        SessionId = sessionId,
        PlayerA = playerA,
        PlayerB = playerB,
        Revision = 3,
        OfferA = new TradeOffer { Gold = 10 },
        OfferB = new TradeOffer { Gold = 20 },
        ConfirmedA = true,
        ConfirmedB = false,
        Status = TradeSessionStatus.Active,
        Location = location
    };

    private static bool PairMatches(
        JsonElement entry,
        string sessionId,
        string playerA,
        string playerB)
    {
        JsonElement facts = entry.GetProperty("facts");
        return facts.GetProperty("session_id").GetString() == sessionId &&
            facts.GetProperty("actor_id").GetString() == playerA &&
            facts.GetProperty("target_id").GetString() == playerB;
    }

    private static DiagnosticSystemInfo SafeSystem() => new(
        "0.5.3",
        "0.5.3+test",
        new string('A', 64),
        "0.111.0",
        "3.4.5",
        "9.0.0",
        "Windows-10.0.26100",
        "X64",
        "en",
        1920,
        1080,
        1920,
        1080);

    private static T Required<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);
        return value!;
    }
}
