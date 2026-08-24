namespace BetterMultiplayer.Diagnostics;

// Fixed-shape facts keep trade diagnostics useful without accepting arbitrary
// player, room, or log text.
internal sealed record DiagnosticTradeFacts(
    string? Location = null,
    bool? Available = null,
    bool? Connected = null,
    bool? Host = null,
    int? Attempt = null,
    int? Count = null,
    int? Revision = null,
    bool? Changed = null,
    bool? Accepted = null,
    bool? Success = null,
    bool? LocalConfirmed = null,
    bool? RemoteConfirmed = null,
    string? Reason = null,
    string? SessionStatus = null);
