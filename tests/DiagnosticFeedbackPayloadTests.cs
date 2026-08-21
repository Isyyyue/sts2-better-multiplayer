using System.Text;
using System.Text.Json;
using BetterMultiplayer.Diagnostics;

namespace BetterMultiplayer.Tests;

public sealed class DiagnosticFeedbackPayloadTests : IDisposable
{
    public void Dispose() => DiagnosticRecorder.ResetForTests();

    [Fact]
    public void RecorderRetainsOnlyTheNewestBoundedEvents()
    {
        DiagnosticRecorder.ResetForTests();
        for (int i = 0; i < DiagnosticRecorder.Capacity + 12; i++)
            DiagnosticRecorder.RecordFeedbackRequested();

        IReadOnlyList<DiagnosticEntry> snapshot = DiagnosticRecorder.Snapshot();

        Assert.Equal(DiagnosticRecorder.Capacity, snapshot.Count);
        Assert.Equal(13, snapshot[0].Sequence);
        Assert.Equal(60, snapshot[^1].Sequence);
    }

    [Fact]
    public void RecorderUsesExactThreeDayRetentionWindow()
    {
        DiagnosticRecorder.ResetForTests();
        DiagnosticRecorder.RecordFeedbackRequested();
        DiagnosticEntry entry = Assert.Single(DiagnosticRecorder.Snapshot());

        IReadOnlyList<DiagnosticEntry> atBoundary = DiagnosticRecorder.Snapshot(
            entry.Timestamp + DiagnosticRecorder.Retention);
        IReadOnlyList<DiagnosticEntry> afterBoundary = DiagnosticRecorder.Snapshot(
            entry.Timestamp + DiagnosticRecorder.Retention + TimeSpan.FromTicks(1));

        Assert.Equal(TimeSpan.FromDays(3), DiagnosticRecorder.Retention);
        Assert.Single(atBoundary);
        Assert.Empty(afterBoundary);
    }

    [Fact]
    public void EventPayloadUsesOnlyWhitelistedDiagnosticData()
    {
        DateTimeOffset createdAt = DateTimeOffset.Parse("2026-08-21T04:00:00Z");
        const string userName = "PrivateWindowsUser";
        const string steamId = "76561198012345678";
        const string password = "room-password-secret";
        string malicious = $"C:\\Users\\{userName}\\save.log {steamId} {password}";
        DiagnosticSystemInfo unsafeSystem = new(
            malicious,
            malicious,
            malicious,
            malicious,
            malicious,
            malicious,
            malicious,
            malicious,
            malicious,
            -10,
            999999,
            -20,
            999999);
        DiagnosticControlState unsafeControl = new(
            new DiagnosticRect(float.NaN, float.PositiveInfinity, -999999, 999999),
            new DiagnosticRect(1, 2, 3, 4),
            true,
            true,
            false,
            true,
            true,
            true,
            false,
            99,
            -99,
            99,
            999999,
            -999999,
            malicious,
            malicious,
            malicious,
            malicious);
        DiagnosticEntry entry = new(
            1,
            createdAt.AddSeconds(-1),
            DiagnosticEventCode.NativeInputMousePressed,
            DiagnosticControlId.MerchantGoldTrade,
            unsafeControl);

        FeedbackEventPayload payload = FeedbackEventFactory.Create(
            [entry],
            unsafeSystem,
            Guid.Parse("fc6d8c0c-43fc-4630-ad85-0ee518f1b9d0"),
            createdAt);
        string json = Encoding.UTF8.GetString(payload.EventBytes);

        Assert.DoesNotContain(userName, json, StringComparison.Ordinal);
        Assert.DoesNotContain(steamId, json, StringComparison.Ordinal);
        Assert.DoesNotContain(password, json, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\\\Users", json, StringComparison.OrdinalIgnoreCase);
        Assert.True(payload.EventBytes.Length <= FeedbackEventFactory.MaxEventBytes);

        using JsonDocument document = JsonDocument.Parse(payload.EventBytes);
        JsonElement root = document.RootElement;
        Assert.False(root.TryGetProperty("user", out _));
        Assert.False(root.TryGetProperty("request", out _));
        Assert.False(root.TryGetProperty("server_name", out _));
        Assert.DoesNotContain("player_count", json, StringComparison.Ordinal);
        Assert.DoesNotContain("network_type", json, StringComparison.Ordinal);
        Assert.Equal(
            "never",
            root.GetProperty("sdk").GetProperty("settings").GetProperty("infer_ip").GetString());
        Assert.Equal(
            "merchant_trade",
            root.GetProperty("tags").GetProperty("report.code").GetString());
        Assert.Equal(
            "external:control",
            root.GetProperty("extra")
                .GetProperty("diagnostics")
                .GetProperty("events")[0]
                .GetProperty("control_state")
                .GetProperty("visual_parent")
                .GetString());
    }

    [Fact]
    public void EventPayloadIncludesExactThreeDayBoundaryAndExcludesOutsideEntries()
    {
        DateTimeOffset createdAt = DateTimeOffset.Parse("2026-08-21T04:00:00Z");
        DiagnosticEntry expired = new(
            1,
            createdAt - DiagnosticRecorder.Retention - TimeSpan.FromMilliseconds(1),
            DiagnosticEventCode.MerchantRoomReady,
            DiagnosticControlId.None,
            null);
        DiagnosticEntry boundary = new(
            2,
            createdAt - DiagnosticRecorder.Retention,
            DiagnosticEventCode.MerchantRoomReady,
            DiagnosticControlId.None,
            null);
        DiagnosticEntry current = new(
            3,
            createdAt - TimeSpan.FromMinutes(1),
            DiagnosticEventCode.FeedbackRequested,
            DiagnosticControlId.SendFeedback,
            null);
        DiagnosticEntry future = new(
            4,
            createdAt + TimeSpan.FromMilliseconds(1),
            DiagnosticEventCode.TradeOverlayShown,
            DiagnosticControlId.None,
            null);

        FeedbackEventPayload payload = FeedbackEventFactory.Create(
            [expired, boundary, current, future],
            SafeSystem(),
            Guid.Parse("fc6d8c0c-43fc-4630-ad85-0ee518f1b9d0"),
            createdAt);

        using JsonDocument document = JsonDocument.Parse(payload.EventBytes);
        JsonElement events = document.RootElement.GetProperty("extra")
            .GetProperty("diagnostics")
            .GetProperty("events");
        Assert.Equal(TimeSpan.FromDays(3), DiagnosticRecorder.Retention);
        Assert.Equal(2, events.GetArrayLength());
        Assert.Equal(2, events[0].GetProperty("sequence").GetInt64());
        Assert.Equal(259_200_000, events[0].GetProperty("age_ms").GetInt64());
        Assert.Equal(3, events[1].GetProperty("sequence").GetInt64());
        Assert.Equal(60_000, events[1].GetProperty("age_ms").GetInt64());
    }

    [Fact]
    public void EnvelopeUsesLfAndUtf8ByteLength()
    {
        const string id = "fc6d8c0c43fc4630ad850ee518f1b9d0";
        byte[] eventBytes = Encoding.UTF8.GetBytes("{\"message\":\"中文反馈\"}");
        FeedbackEventPayload payload = new(
            id,
            DateTimeOffset.Parse("2026-08-21T04:00:00Z"),
            eventBytes);

        byte[] envelope = SentryEnvelopeSerializer.Serialize(
            payload,
            DateTimeOffset.Parse("2026-08-21T04:00:01Z"));
        string text = Encoding.UTF8.GetString(envelope);
        string[] lines = text.Split('\n');

        Assert.DoesNotContain('\r', text);
        Assert.Equal(4, lines.Length);
        Assert.Equal(string.Empty, lines[^1]);
        using JsonDocument envelopeHeader = JsonDocument.Parse(lines[0]);
        using JsonDocument itemHeader = JsonDocument.Parse(lines[1]);
        Assert.Equal(id, envelopeHeader.RootElement.GetProperty("event_id").GetString());
        Assert.Equal(eventBytes.Length, itemHeader.RootElement.GetProperty("length").GetInt32());
        Assert.Equal("event", itemHeader.RootElement.GetProperty("type").GetString());
        Assert.Equal(Encoding.UTF8.GetString(eventBytes), lines[2]);
    }

    private static DiagnosticSystemInfo SafeSystem() => new(
        "0.5.0",
        "0.5.0+test",
        new string('A', 64),
        "0.107.1",
        "3.3.7",
        "9.0.0",
        "Windows-10.0.26100",
        "X64",
        "en",
        1920,
        1080,
        1920,
        1080);
}
