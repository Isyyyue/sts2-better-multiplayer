using System.Collections.ObjectModel;
using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using BetterMultiplayer.Trading;

namespace BetterMultiplayer.Diagnostics;

internal enum DiagnosticEventCode
{
    MerchantRoomReady,
    MerchantButtonAdded,
    NativeInputFocused,
    NativeInputUnfocused,
    NativeInputMousePressed,
    NativeInputMouseReleased,
    NativeInputReleased,
    TradeOverlayRequested,
    TradeOverlayShown,
    TradeLocationStarted,
    TradeStateReset,
    TradeAvailabilitySent,
    TradeAvailabilityHandled,
    TradeAvailabilityReceived,
    TradeWaitingForPlayers,
    TradeInviteRequested,
    TradeInviteRejected,
    TradeSessionChanged,
    FeedbackRequested
}

internal enum DiagnosticControlId
{
    None,
    MerchantGoldTrade,
    SendFeedback
}

internal sealed record DiagnosticRect(float X, float Y, float Width, float Height);

internal sealed record DiagnosticControlState(
    DiagnosticRect VisualRect,
    DiagnosticRect InputRect,
    bool VisualVisible,
    bool VisualVisibleInTree,
    bool VisualDisabled,
    bool InputVisible,
    bool InputVisibleInTree,
    bool InputEnabled,
    bool InputHasFocus,
    int VisualMouseFilter,
    int InputMouseFilter,
    int InputFocusMode,
    int VisualZIndex,
    int InputZIndex,
    string VisualParent,
    string HoveredControl,
    string FocusOwner,
    string ActiveScreen);

internal sealed record DiagnosticEntry(
    long Sequence,
    DateTimeOffset Timestamp,
    DiagnosticEventCode Code,
    DiagnosticControlId Control,
    DiagnosticControlState? ControlState,
    DiagnosticTradeFacts? Facts = null);

internal static class DiagnosticRecorder
{
    internal const int Capacity = 96;
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(3);

    private static readonly object Gate = new();
    private static readonly Queue<DiagnosticEntry> Entries = new(Capacity);
    private static readonly Dictionary<AvailabilityHandledKey, int> AvailabilityHandledOccurrences = [];
    private static long _sequence;

    private readonly record struct AvailabilityHandledKey(
        TradeLocation Location,
        bool Available,
        bool Accepted,
        bool Changed,
        string Reason);

    internal static void RecordMerchantRoom() =>
        Record(DiagnosticEventCode.MerchantRoomReady, facts: Facts(TradeLocation.Merchant));

    internal static void RecordTradeOverlayRequested(
        TradeLocation? location = TradeLocation.Merchant) =>
        Record(DiagnosticEventCode.TradeOverlayRequested, facts: Facts(location));

    internal static void RecordTradeOverlayShown(
        TradeLocation? location = TradeLocation.Merchant) =>
        Record(DiagnosticEventCode.TradeOverlayShown, facts: Facts(location));

    internal static void RecordTradeLocationStarted(
        TradeLocation location,
        bool duplicate,
        int priorSessionCount = 0)
    {
        if (!duplicate)
            ResetAvailabilitySampling();
        Record(
            DiagnosticEventCode.TradeLocationStarted,
            facts: Facts(
                location,
                count: priorSessionCount,
                changed: !duplicate,
                reason: duplicate ? "duplicate" : "initialized"));
    }

    internal static void RecordTradeStateReset(
        TradeLocation? location,
        string reason,
        int clearedSessionCount = 0,
        bool changed = true)
    {
        if (changed)
            ResetAvailabilitySampling();
        Record(
            DiagnosticEventCode.TradeStateReset,
            facts: Facts(
                location,
                count: clearedSessionCount,
                changed: changed,
                reason: SafeReason(reason)));
    }

    internal static void RecordAvailabilitySent(
        TradeLocation location,
        bool available,
        bool connected,
        bool host,
        int attempt,
        bool? success = null)
    {
        if (available && !ShouldRecordAttempt(attempt))
            return;
        Record(
            DiagnosticEventCode.TradeAvailabilitySent,
            facts: Facts(
                location,
                available,
                connected,
                host,
                attempt: attempt,
                success: success));
    }

    internal static void RecordAvailabilityHandled(
        TradeLocation location,
        bool available,
        bool accepted,
        bool changed,
        string reason,
        bool? connected = null,
        bool? host = true)
    {
        string safeReason = SafeReason(accepted ? "accepted" : reason);
        if (!changed && !ShouldRecordAvailabilityHandled(new AvailabilityHandledKey(
                location,
                available,
                accepted,
                changed,
                safeReason)))
            return;
        Record(
            DiagnosticEventCode.TradeAvailabilityHandled,
            facts: Facts(
                location,
                available,
                connected,
                host,
                changed: changed,
                accepted: accepted,
                reason: safeReason));
    }

    internal static void RecordAvailabilityReceived(
        TradeLocation location,
        bool available,
        bool changed,
        bool? connected = null)
    {
        if (!changed)
            return;
        Record(
            DiagnosticEventCode.TradeAvailabilityReceived,
            facts: Facts(
                location,
                available,
                connected,
                changed: changed,
                accepted: true,
                reason: "host_event"));
    }

    internal static void RecordWaitingForPlayers(TradeLocation location, bool hasPlayers) =>
        Record(
            DiagnosticEventCode.TradeWaitingForPlayers,
            facts: Facts(
                location,
                available: false,
                connected: CanReadNetworkConnection(),
                host: CanReadHost(),
                reason: hasPlayers ? "no_available_peer" : "no_other_players"));

    internal static void RecordInviteRequested(
        TradeLocation location,
        bool? available = null,
        bool? connected = null) =>
        Record(
            DiagnosticEventCode.TradeInviteRequested,
            facts: Facts(location, available, connected, accepted: null));

    internal static void RecordInviteRejected(TradeLocation location, string reason) =>
        Record(
            DiagnosticEventCode.TradeInviteRejected,
            facts: Facts(
                location,
                accepted: false,
                success: false,
                reason: SafeReason(reason)));

    internal static void RecordSessionChanged(
        TradeLocation location,
        string status,
        bool changed = true,
        int revision = 0,
        bool? localConfirmed = null,
        bool? remoteConfirmed = null,
        bool? success = null) =>
        Record(
            DiagnosticEventCode.TradeSessionChanged,
            facts: Facts(
                location,
                revision: revision,
                changed: changed,
                success: success,
                localConfirmed: localConfirmed,
                remoteConfirmed: remoteConfirmed,
                sessionStatus: SafeSessionStatus(status)));

    internal static void RecordFeedbackRequested() =>
        Record(DiagnosticEventCode.FeedbackRequested, DiagnosticControlId.SendFeedback);

    internal static void RecordControl(
        string stage,
        string diagnosticId,
        Button visual,
        NButton input)
    {
        if (!TryMapControl(diagnosticId, out DiagnosticControlId control) ||
            !TryMapStage(stage, out DiagnosticEventCode code))
            return;

        DiagnosticControlState state;
        try
        {
            Viewport? viewport = visual.GetViewport();
            state = new DiagnosticControlState(
                Rect(visual.GetGlobalRect()),
                Rect(input.GetGlobalRect()),
                visual.Visible,
                visual.IsVisibleInTree(),
                visual.Disabled,
                input.Visible,
                input.IsVisibleInTree(),
                input.IsEnabled,
                input.HasFocus(),
                (int)visual.MouseFilter,
                (int)input.MouseFilter,
                (int)input.FocusMode,
                visual.ZIndex,
                input.ZIndex,
                ClassifyNode(visual.GetParent()),
                ClassifyNode(viewport?.GuiGetHoveredControl()),
                ClassifyNode(viewport?.GuiGetFocusOwner()),
                ClassifyNode(ActiveScreenContext.Instance.GetCurrentScreen() as GodotObject));
        }
        catch
        {
            return;
        }

        Record(code, control, controlState: state);
    }

    internal static void RecordMerchantButtonAdded(Button visual, NButton input) =>
        RecordControl("added", "merchant_gold_trade", visual, input);

    internal static IReadOnlyList<DiagnosticEntry> Snapshot() =>
        Snapshot(DateTimeOffset.UtcNow);

    internal static IReadOnlyList<DiagnosticEntry> Snapshot(DateTimeOffset now)
    {
        lock (Gate)
        {
            DateTimeOffset cutoff = now.ToUniversalTime() - Retention;
            while (Entries.TryPeek(out DiagnosticEntry? entry) && entry.Timestamp < cutoff)
                Entries.Dequeue();

            return new ReadOnlyCollection<DiagnosticEntry>(Entries.ToArray());
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            Entries.Clear();
            AvailabilityHandledOccurrences.Clear();
            _sequence = 0;
        }
    }

    private static bool ShouldRecordAttempt(int attempt) =>
        attempt <= 3 || IsPowerOfTwo(attempt);

    private static bool ShouldRecordAvailabilityHandled(AvailabilityHandledKey key)
    {
        lock (Gate)
        {
            int occurrence = AvailabilityHandledOccurrences.GetValueOrDefault(key) + 1;
            AvailabilityHandledOccurrences[key] = occurrence;
            return occurrence <= 3 || IsPowerOfTwo(occurrence);
        }
    }

    private static bool IsPowerOfTwo(int value) =>
        value > 0 && (value & (value - 1)) == 0;

    private static void ResetAvailabilitySampling()
    {
        lock (Gate)
            AvailabilityHandledOccurrences.Clear();
    }

    private static void Record(
        DiagnosticEventCode code,
        DiagnosticControlId control = DiagnosticControlId.None,
        DiagnosticControlState? controlState = null,
        DiagnosticTradeFacts? facts = null)
    {
        lock (Gate)
        {
            while (Entries.Count >= Capacity)
                Entries.Dequeue();

            Entries.Enqueue(new DiagnosticEntry(
                ++_sequence,
                DateTimeOffset.UtcNow,
                code,
                control,
                controlState,
                facts));
        }
    }

    private static DiagnosticTradeFacts Facts(
        TradeLocation? location,
        bool? available = null,
        bool? connected = null,
        bool? host = null,
        int? attempt = null,
        int? count = null,
        int? revision = null,
        bool? changed = null,
        bool? accepted = null,
        bool? success = null,
        bool? localConfirmed = null,
        bool? remoteConfirmed = null,
        string? reason = null,
        string? sessionStatus = null) =>
        new(
            Location: location.HasValue ? Location(location.Value) : null,
            Available: available,
            Connected: connected,
            Host: host,
            Attempt: attempt.HasValue ? Math.Clamp(attempt.Value, 0, 255) : null,
            Count: count.HasValue ? Math.Clamp(count.Value, 0, 255) : null,
            Revision: revision.HasValue ? Math.Clamp(revision.Value, 0, 1_000_000) : null,
            Changed: changed,
            Accepted: accepted,
            Success: success,
            LocalConfirmed: localConfirmed,
            RemoteConfirmed: remoteConfirmed,
            Reason: reason,
            SessionStatus: sessionStatus);

    private static string Location(TradeLocation location) => location switch
    {
        TradeLocation.Merchant => "merchant",
        TradeLocation.RestSite => "rest_site",
        _ => "unknown"
    };

    private static string SafeReason(string value) => value switch
    {
        "initialized" or "duplicate" or "location_changed" or "cleanup" or "superseded_owner_exit" or
        "accepted" or "not_available" or "no_active_location" or "invalid_location" or "not_run_player" or "invalid_gold" or
        "wrong_location" or "no_available_peer" or "no_other_players" or "host_event" or
        "not_connected" or "already_trading" or "used" or "disconnected" or
        "declined" or "expired" or "invalid_session" or "revision_mismatch" or
        "player_missing" or "validation_failed" or "apply_failed" or "completed" or
        "canceled" => value,
        _ => "other"
    };

    private static string SafeSessionStatus(string value) => value switch
    {
        "pending" or "active" or "committing" or "committed" or "canceled" => value,
        _ => "unknown"
    };

    private static bool CanReadNetworkConnection()
    {
        try
        {
            return MegaCrit.Sts2.Core.Runs.RunManager.Instance.IsInProgress &&
                MegaCrit.Sts2.Core.Runs.RunManager.Instance.NetService.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanReadHost()
    {
        try
        {
            return MegaCrit.Sts2.Core.Runs.RunManager.Instance.NetService.Type ==
                MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryMapControl(string id, out DiagnosticControlId control)
    {
        control = id switch
        {
            "merchant_gold_trade" => DiagnosticControlId.MerchantGoldTrade,
            "send_feedback" => DiagnosticControlId.SendFeedback,
            _ => DiagnosticControlId.None
        };
        return control != DiagnosticControlId.None;
    }

    private static bool TryMapStage(string stage, out DiagnosticEventCode code)
    {
        code = stage switch
        {
            "added" => DiagnosticEventCode.MerchantButtonAdded,
            "focused" => DiagnosticEventCode.NativeInputFocused,
            "unfocused" => DiagnosticEventCode.NativeInputUnfocused,
            "mouse_pressed" => DiagnosticEventCode.NativeInputMousePressed,
            "mouse_released" => DiagnosticEventCode.NativeInputMouseReleased,
            "released" => DiagnosticEventCode.NativeInputReleased,
            _ => default
        };
        return stage is "added" or "focused" or "unfocused" or
            "mouse_pressed" or "mouse_released" or "released";
    }

    private static DiagnosticRect Rect(Rect2 rect) => new(
        Round(rect.Position.X),
        Round(rect.Position.Y),
        Round(rect.Size.X),
        Round(rect.Size.Y));

    private static float Round(float value) =>
        float.IsFinite(value) ? MathF.Round(value, 2) : 0f;

    private static string ClassifyNode(GodotObject? value)
    {
        if (value is null || !GodotObject.IsInstanceValid(value))
            return "none";

        Type type = value.GetType();
        string assembly = type.Assembly.GetName().Name ?? string.Empty;
        string origin = assembly switch
        {
            "sts2" => "game",
            "GodotSharp" => "godot",
            "BetterMultiplayer" => "mod",
            _ => "external"
        };
        string typeName = origin == "external" ? "control" : SafeTypeName(type.Name);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{origin}:{typeName}");
    }

    private static string SafeTypeName(string value)
    {
        Span<char> buffer = stackalloc char[Math.Min(value.Length, 64)];
        int length = 0;
        foreach (char c in value)
        {
            if (length >= buffer.Length)
                break;
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '`')
                buffer[length++] = c;
        }
        return length == 0 ? "unknown" : new string(buffer[..length]);
    }
}
