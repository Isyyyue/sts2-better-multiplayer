using System.Collections.ObjectModel;
using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

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
    DiagnosticControlState? ControlState);

internal static class DiagnosticRecorder
{
    internal const int Capacity = 48;
    internal static readonly TimeSpan Retention = TimeSpan.FromMinutes(30);

    private static readonly object Gate = new();
    private static readonly Queue<DiagnosticEntry> Entries = new(Capacity);
    private static long _sequence;

    internal static void RecordMerchantRoom() =>
        Record(DiagnosticEventCode.MerchantRoomReady);

    internal static void RecordTradeOverlayRequested() =>
        Record(DiagnosticEventCode.TradeOverlayRequested);

    internal static void RecordTradeOverlayShown() =>
        Record(DiagnosticEventCode.TradeOverlayShown);

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
            _sequence = 0;
        }
    }

    private static void Record(
        DiagnosticEventCode code,
        DiagnosticControlId control = DiagnosticControlId.None,
        DiagnosticControlState? controlState = null)
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
                controlState));
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
