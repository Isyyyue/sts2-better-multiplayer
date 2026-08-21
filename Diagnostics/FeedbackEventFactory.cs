using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterMultiplayer.Diagnostics;

internal sealed record FeedbackEventPayload(
    string EventId,
    DateTimeOffset CreatedAt,
    byte[] EventBytes);

internal static class FeedbackEventFactory
{
    internal const int MaxEventBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    internal static FeedbackEventPayload Create(
        IReadOnlyList<DiagnosticEntry> snapshot,
        DiagnosticSystemInfo system,
        Guid eventId,
        DateTimeOffset createdAt)
    {
        system = system.Normalize();
        string id = eventId.ToString("N").ToLowerInvariant();
        DateTimeOffset timestamp = createdAt.ToUniversalTime();
        DateTimeOffset cutoff = timestamp - DiagnosticRecorder.Retention;
        List<DiagnosticEntry> included = snapshot
            .Where(entry => entry.Timestamp.ToUniversalTime() >= cutoff &&
                entry.Timestamp.ToUniversalTime() <= timestamp)
            .TakeLast(DiagnosticRecorder.Capacity)
            .ToList();

        while (true)
        {
            SentryEventDto sentryEvent = BuildEvent(id, timestamp, system, included);
            byte[] eventBytes = JsonSerializer.SerializeToUtf8Bytes(sentryEvent, JsonOptions);
            if (eventBytes.Length <= MaxEventBytes)
                return new FeedbackEventPayload(id, timestamp, eventBytes);
            if (included.Count == 0)
                throw new InvalidOperationException("The fixed diagnostic event exceeds the local size limit.");
            included.RemoveAt(0);
        }
    }

    private static SentryEventDto BuildEvent(
        string eventId,
        DateTimeOffset createdAt,
        DiagnosticSystemInfo system,
        IReadOnlyList<DiagnosticEntry> entries)
    {
        string area = entries.Any(IsMerchantEvent) ? "merchant_trade" : "general";
        List<DiagnosticEntryDto> eventDtos = entries
            .Select(entry => Entry(entry, createdAt))
            .ToList();

        return new SentryEventDto(
            eventId,
            Timestamp(createdAt),
            "csharp",
            "error",
            "BetterMultiplayer.PlayerReport",
            $"better-multiplayer@{DiagnosticSystemInfo.SafeVersion(system.ModVersion)}",
            "production",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["report.kind"] = "manual",
                ["report.code"] = area,
                ["mod.version"] = DiagnosticSystemInfo.SafeVersion(system.ModVersion),
                ["game.version"] = DiagnosticSystemInfo.SafeVersion(system.GameBuild),
                ["baselib.version"] = DiagnosticSystemInfo.SafeVersion(system.BaseLibVersion)
            },
            ["better-multiplayer-feedback", area],
            new LogEntryDto("Player submitted Better Multiplayer diagnostics"),
            new ExtraDto(new DiagnosticReportDto(
                "One click uploads once; no screenshots, saves, logs, player IDs, room data, or user text.",
                system,
                eventDtos)),
            new SdkDto(
                "isyyyue.csharp.better-multiplayer",
                DiagnosticSystemInfo.SafeVersion(system.ModVersion),
                new SdkSettingsDto("never")));
    }

    private static DiagnosticEntryDto Entry(DiagnosticEntry entry, DateTimeOffset createdAt)
    {
        long ageMs = (long)Math.Max(
            (createdAt - entry.Timestamp.ToUniversalTime()).TotalMilliseconds,
            0);
        return new DiagnosticEntryDto(
            Math.Max(entry.Sequence, 0),
            ageMs,
            Code(entry.Code),
            Control(entry.Control),
            Normalize(entry.ControlState));
    }

    private static DiagnosticControlState? Normalize(DiagnosticControlState? state)
    {
        if (state is null)
            return null;

        return state with
        {
            VisualRect = Normalize(state.VisualRect),
            InputRect = Normalize(state.InputRect),
            VisualMouseFilter = Math.Clamp(state.VisualMouseFilter, 0, 2),
            InputMouseFilter = Math.Clamp(state.InputMouseFilter, 0, 2),
            InputFocusMode = Math.Clamp(state.InputFocusMode, 0, 2),
            VisualZIndex = Math.Clamp(state.VisualZIndex, -4096, 4096),
            InputZIndex = Math.Clamp(state.InputZIndex, -4096, 4096),
            VisualParent = SafeNode(state.VisualParent),
            HoveredControl = SafeNode(state.HoveredControl),
            FocusOwner = SafeNode(state.FocusOwner),
            ActiveScreen = SafeNode(state.ActiveScreen)
        };
    }

    private static DiagnosticRect Normalize(DiagnosticRect rect) => new(
        SafeCoordinate(rect.X),
        SafeCoordinate(rect.Y),
        SafeCoordinate(rect.Width),
        SafeCoordinate(rect.Height));

    private static float SafeCoordinate(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, -32768f, 32768f) : 0f;

    private static string SafeNode(string value)
    {
        if (value is "none" or "external:control")
            return value;
        int separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
            return "external:control";
        string origin = value[..separator];
        string type = value[(separator + 1)..];
        if (origin is not ("game" or "godot" or "mod") ||
            type.Length > 64 ||
            !type.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '`'))
            return "external:control";
        return value;
    }

    private static bool IsMerchantEvent(DiagnosticEntry entry) =>
        entry.Code is DiagnosticEventCode.MerchantRoomReady or
            DiagnosticEventCode.MerchantButtonAdded or
            DiagnosticEventCode.TradeOverlayRequested or
            DiagnosticEventCode.TradeOverlayShown ||
        entry.Control == DiagnosticControlId.MerchantGoldTrade;

    private static string Code(DiagnosticEventCode value) => value switch
    {
        DiagnosticEventCode.MerchantRoomReady => "merchant.room_ready",
        DiagnosticEventCode.MerchantButtonAdded => "merchant.button_added",
        DiagnosticEventCode.NativeInputFocused => "native_input.focused",
        DiagnosticEventCode.NativeInputUnfocused => "native_input.unfocused",
        DiagnosticEventCode.NativeInputMousePressed => "native_input.mouse_pressed",
        DiagnosticEventCode.NativeInputMouseReleased => "native_input.mouse_released",
        DiagnosticEventCode.NativeInputReleased => "native_input.released",
        DiagnosticEventCode.TradeOverlayRequested => "trade_overlay.requested",
        DiagnosticEventCode.TradeOverlayShown => "trade_overlay.shown",
        DiagnosticEventCode.FeedbackRequested => "feedback.requested",
        _ => "unknown"
    };

    private static string Control(DiagnosticControlId value) => value switch
    {
        DiagnosticControlId.MerchantGoldTrade => "merchant_gold_trade",
        DiagnosticControlId.SendFeedback => "send_feedback",
        _ => "none"
    };

    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture);

    private sealed record SentryEventDto(
        string EventId,
        string Timestamp,
        string Platform,
        string Level,
        string Logger,
        string Release,
        string Environment,
        IReadOnlyDictionary<string, string> Tags,
        IReadOnlyList<string> Fingerprint,
        LogEntryDto Logentry,
        ExtraDto Extra,
        SdkDto Sdk);

    private sealed record LogEntryDto(string Formatted);

    private sealed record ExtraDto(DiagnosticReportDto Diagnostics);

    private sealed record DiagnosticReportDto(
        string Privacy,
        DiagnosticSystemInfo System,
        IReadOnlyList<DiagnosticEntryDto> Events);

    private sealed record DiagnosticEntryDto(
        long Sequence,
        long AgeMs,
        string Code,
        string Control,
        DiagnosticControlState? ControlState);

    private sealed record SdkDto(
        string Name,
        string Version,
        SdkSettingsDto Settings);

    private sealed record SdkSettingsDto(string InferIp);
}

internal static class SentryEnvelopeSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    internal static byte[] Serialize(FeedbackEventPayload payload, DateTimeOffset sentAt)
    {
        byte[] header = JsonSerializer.SerializeToUtf8Bytes(
            new EnvelopeHeader(
                payload.EventId,
                sentAt.ToUniversalTime().ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    CultureInfo.InvariantCulture)),
            JsonOptions);
        byte[] itemHeader = JsonSerializer.SerializeToUtf8Bytes(
            new EnvelopeItemHeader("event", "application/json", payload.EventBytes.Length),
            JsonOptions);

        byte[] envelope = new byte[
            header.Length + 1 + itemHeader.Length + 1 + payload.EventBytes.Length + 1];
        int offset = 0;
        Copy(header, envelope, ref offset);
        envelope[offset++] = (byte)'\n';
        Copy(itemHeader, envelope, ref offset);
        envelope[offset++] = (byte)'\n';
        Copy(payload.EventBytes, envelope, ref offset);
        envelope[offset] = (byte)'\n';
        return envelope;
    }

    private static void Copy(byte[] source, byte[] destination, ref int offset)
    {
        Buffer.BlockCopy(source, 0, destination, offset, source.Length);
        offset += source.Length;
    }

    private sealed record EnvelopeHeader(string EventId, string SentAt);

    private sealed record EnvelopeItemHeader(string Type, string ContentType, int Length);
}
