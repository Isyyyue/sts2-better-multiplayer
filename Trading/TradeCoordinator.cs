using System.Security.Cryptography;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using BetterMultiplayer.Trading.Messages;
using BetterMultiplayer.Localization;
using BetterMultiplayer.Diagnostics;

namespace BetterMultiplayer.Trading;

internal static class TradeCoordinator
{
    private static readonly Dictionary<ulong, TradeLocation> AvailablePlayers = [];
    private static readonly Dictionary<ulong, int> AvailableGold = [];
    private static readonly Dictionary<ulong, TradeSessionSnapshot> Sessions = [];
    private static readonly Dictionary<ulong, ulong> SessionByPlayer = [];
    private static TradeLocation? _activeLocation;
    private static ulong? _activeLocationOwner;

    internal static void SetAvailable(ulong playerId, bool available, TradeLocation location, int reportedGold)
    {
        if (!Enum.IsDefined(location))
        {
            DiagnosticRecorder.RecordAvailabilityHandled(
                location,
                available,
                accepted: false,
                changed: false,
                reason: "invalid_location");
            return;
        }

        if (_activeLocation is null)
        {
            // A late message from an overlay that is already leaving must not
            // reopen a location after its lifecycle cleanup ran.
            DiagnosticRecorder.RecordAvailabilityHandled(
                location,
                available,
                accepted: false,
                changed: false,
                reason: "no_active_location");
            return;
        }
        else if (_activeLocation != location)
        {
            DiagnosticRecorder.RecordAvailabilityHandled(
                location,
                available,
                accepted: false,
                changed: false,
                reason: "wrong_location");
            return;
        }

        if (!IsRunPlayer(playerId))
        {
            DiagnosticRecorder.RecordAvailabilityHandled(
                location,
                available,
                accepted: false,
                changed: false,
                reason: "not_run_player");
            return;
        }

        if (available && location == TradeLocation.Merchant &&
            !TradeGoldBalance.TryValidateOffer(reportedGold, 0, out string goldError))
        {
            DiagnosticRecorder.RecordAvailabilityHandled(
                location,
                available,
                accepted: false,
                changed: false,
                reason: "invalid_gold");
            Error(playerId, goldError);
            return;
        }

        bool changed = available
            ? !AvailablePlayers.TryGetValue(playerId, out TradeLocation current) ||
                current != location ||
                (location == TradeLocation.Merchant &&
                    AvailableGold.GetValueOrDefault(playerId) != reportedGold)
            : AvailablePlayers.TryGetValue(playerId, out TradeLocation existing) &&
                existing == location;

        if (available)
        {
            AvailablePlayers[playerId] = location;
            AvailableGold[playerId] = location == TradeLocation.Merchant ? reportedGold : 0;
        }
        else if (AvailablePlayers.TryGetValue(playerId, out TradeLocation existingLocation) &&
            existingLocation == location)
        {
            AvailablePlayers.Remove(playerId);
            AvailableGold.Remove(playerId);
            CancelForPlayer(playerId);
        }

        TradeNetwork.Broadcast(new AvailabilityEvent
        {
            PlayerId = playerId,
            Available = available,
            Location = location
        });
        DiagnosticRecorder.RecordAvailabilityHandled(
            location,
            available,
            accepted: true,
            changed: changed,
            reason: "accepted");

        // Replay the current host view so a peer that missed an earlier event
        // can recover without reopening the overlay.
        if (available)
            BroadcastAvailabilitySnapshot();
    }

    internal static void Invite(ulong senderId, ulong targetId)
    {
        if (senderId == targetId ||
            !AvailablePlayers.TryGetValue(senderId, out TradeLocation location) ||
            !AvailablePlayers.TryGetValue(targetId, out TradeLocation targetLocation) ||
            location != targetLocation ||
            !IsConnected(targetId))
        {
            DiagnosticRecorder.RecordInviteRejected(
                _activeLocation ?? TradeLocation.Merchant,
                "not_available");
            Error(senderId, ModText.Token(TextKey.PartnerNotAtTradeScreen));
            return;
        }

        if (location == TradeLocation.RestSite &&
            (TradeUsageTracker.HasUsed(senderId) || TradeUsageTracker.HasUsed(targetId)))
        {
            DiagnosticRecorder.RecordInviteRejected(location, "used");
            Error(senderId, ModText.Token(TextKey.RestSiteTradeAlreadyUsed));
            return;
        }

        if (SessionByPlayer.ContainsKey(senderId) || SessionByPlayer.ContainsKey(targetId))
        {
            DiagnosticRecorder.RecordInviteRejected(location, "already_trading");
            Error(senderId, ModText.Token(TextKey.PlayerAlreadyTrading));
            return;
        }

        ulong sessionId = CreateSessionId();
        TradeSessionSnapshot snapshot = new()
        {
            SessionId = sessionId,
            PlayerA = senderId,
            PlayerB = targetId,
            GoldA = AvailableGold.GetValueOrDefault(senderId),
            GoldB = AvailableGold.GetValueOrDefault(targetId),
            Status = TradeSessionStatus.Pending,
            Location = location
        };

        Sessions[sessionId] = snapshot;
        SessionByPlayer[senderId] = sessionId;
        SessionByPlayer[targetId] = sessionId;
        DiagnosticRecorder.RecordSessionSnapshot(
            snapshot,
            "pending",
            senderId,
            targetId);
        BroadcastSnapshot(snapshot);
    }

    internal static void RespondToInvite(ulong senderId, ulong sessionId, bool accepted, int reportedGold)
    {
        if (!Sessions.TryGetValue(sessionId, out TradeSessionSnapshot? session) ||
            session.Status != TradeSessionStatus.Pending ||
            senderId != session.PlayerB)
        {
            Error(senderId, ModText.Token(TextKey.TradeInviteExpired));
            return;
        }

        if (!accepted)
        {
            EndSession(session, TradeSessionStatus.Canceled);
            return;
        }

        if (session.Location == TradeLocation.Merchant)
        {
            if (!TradeGoldBalance.TryValidateOffer(reportedGold, 0, out string error))
            {
                Error(senderId, error);
                return;
            }
            Player? player = RunManager.Instance.State?.GetPlayer(senderId);
            if (player is null)
            {
                Error(senderId, ModText.Token(TextKey.PlayerNotFound));
                return;
            }
            session.SetGold(senderId, player.Gold);
            AvailableGold[senderId] = session.GoldFor(senderId);
        }

        session.Status = TradeSessionStatus.Active;
        session.Revision++;
        DiagnosticRecorder.RecordSessionSnapshot(
            session,
            "active",
            senderId,
            session.OtherPlayer(senderId));
        BroadcastSnapshot(session);
    }

    internal static void UpdateOffer(ulong senderId, ulong sessionId, TradeOffer rawOffer, int reportedGold)
    {
        if (!TryGetActiveParticipant(senderId, sessionId, out TradeSessionSnapshot? session))
            return;

        Player? player = RunManager.Instance.State?.GetPlayer(senderId);
        if (player is null)
        {
            Error(senderId, ModText.Token(TextKey.PlayerNotFound));
            return;
        }
        int availableGold = player.Gold;
        if (session.Location == TradeLocation.Merchant)
        {
            if (!TradeGoldBalance.TryValidateOffer(reportedGold, rawOffer.Gold, out string goldError))
            {
                Error(senderId, goldError);
                return;
            }
            availableGold = player.Gold;
            session.SetGold(senderId, player.Gold);
            AvailableGold[senderId] = player.Gold;
        }
        if (!TradeValidator.TryResolve(player, rawOffer, session.Location, availableGold, out _, out string error))
        {
            Error(senderId, error.Length == 0 ? ModText.Token(TextKey.InvalidOffer) : error);
            return;
        }

        TradeOffer offer = rawOffer.Normalized();
        if (senderId == session.PlayerA)
            session.OfferA = offer;
        else
            session.OfferB = offer;
        session.BumpOfferRevision(senderId);
        session.SetLocked(senderId, false, 0);
        session.Revision++;
        DiagnosticRecorder.RecordSessionSnapshot(
            session,
            "offer_updated",
            senderId,
            session.OtherPlayer(senderId));
        BroadcastSnapshot(session);
    }

    internal static void Confirm(
        ulong senderId,
        ulong sessionId,
        int revision,
        bool confirmed,
        int reportedGold)
    {
        if (!TryGetActiveParticipant(senderId, sessionId, out TradeSessionSnapshot? session))
            return;
        if (revision != session.OfferRevisionFor(senderId))
        {
            Error(senderId, ModText.Token(TextKey.OfferChanged));
            BroadcastSnapshot(session);
            return;
        }

        if (session.Location == TradeLocation.Merchant)
        {
            if (!TradeGoldBalance.TryValidateOffer(
                    reportedGold,
                    session.OfferFor(senderId).Gold,
                    out string goldError))
            {
                Error(senderId, goldError);
                return;
            }
            if (session.GoldFor(senderId) != reportedGold)
            {
                Player? currentPlayer = RunManager.Instance.State?.GetPlayer(senderId);
                if (currentPlayer is null)
                {
                    Error(senderId, ModText.Token(TextKey.PlayerNotFound));
                    return;
                }
                int authoritativeGold = currentPlayer.Gold;
                session.SetGold(senderId, authoritativeGold);
                AvailableGold[senderId] = authoritativeGold;
                session.BumpOfferRevision(senderId);
                session.SetLocked(senderId, false, 0);
                session.Revision++;
                if (confirmed)
                    Error(senderId, ModText.Token(TextKey.GoldBalanceChanged));
                BroadcastSnapshot(session);
                return;
            }
        }

        session.SetLocked(senderId, confirmed, revision);
        DiagnosticRecorder.RecordSessionSnapshot(
            session,
            "confirmed",
            senderId,
            session.OtherPlayer(senderId));
        BroadcastSnapshot(session);

        if (session.LockedA && session.LockedB)
            TaskHelper.RunSafely(Commit(session));
    }

    internal static void Cancel(ulong senderId, ulong sessionId)
    {
        if (Sessions.TryGetValue(sessionId, out TradeSessionSnapshot? session) && session.Contains(senderId))
            EndSession(session, TradeSessionStatus.Canceled);
    }

    internal static void PlayerDisconnected(ulong playerId)
    {
        AssistSmithCoordinator.PlayerDisconnected(playerId);
        TradeLocation location = AvailablePlayers.GetValueOrDefault(playerId);
        if (SessionByPlayer.TryGetValue(playerId, out ulong sessionId) &&
            Sessions.TryGetValue(sessionId, out TradeSessionSnapshot? session))
        {
            location = session.Location;
        }
        if (!AvailablePlayers.Remove(playerId) && !SessionByPlayer.ContainsKey(playerId))
            return;
        AvailableGold.Remove(playerId);

        CancelForPlayer(playerId);
        DiagnosticRecorder.RecordAvailabilityHandled(
            location,
            available: false,
            accepted: true,
            changed: true,
            reason: "disconnected");
        TradeNetwork.Broadcast(new AvailabilityEvent
        {
            PlayerId = playerId,
            Available = false,
            Location = location
        });
    }

    internal static void BeginLocation(TradeLocation location) =>
        BeginLocation(location, ownerId: null);

    internal static void BeginLocation(TradeLocation location, ulong ownerId) =>
        BeginLocation(location, (ulong?)ownerId);

    private static void BeginLocation(TradeLocation location, ulong? ownerId)
    {
        if (_activeLocation == location)
        {
            if (ownerId.HasValue)
                _activeLocationOwner = ownerId.Value;
            DiagnosticRecorder.RecordTradeLocationStarted(location, duplicate: true);
            return;
        }

        TradeLocation? previous = _activeLocation;
        _activeLocation = location;
        _activeLocationOwner = ownerId;
        DiagnosticRecorder.RecordTradeLocationStarted(location, duplicate: false);
        foreach (TradeSessionSnapshot session in Sessions.Values.ToList())
            EndSession(session, TradeSessionStatus.Canceled);
        AvailablePlayers.Clear();
        AvailableGold.Clear();
        Sessions.Clear();
        SessionByPlayer.Clear();
        TradeStateStore.Reset();
        DiagnosticRecorder.RecordTradeStateReset(
            location,
            previous.HasValue ? "location_changed" : "initialized");
        if (location == TradeLocation.RestSite)
        {
            TradeUsageTracker.BeginRestSite();
            TradeRestSiteFlow.BeginRestSite();
            AssistSmithCoordinator.BeginRestSite();
        }
    }

    internal static void EndLocation(TradeLocation location) =>
        EndLocation(location, ownerId: null);

    internal static void EndLocation(TradeLocation location, ulong ownerId) =>
        EndLocation(location, (ulong?)ownerId);

    private static void EndLocation(TradeLocation location, ulong? ownerId)
    {
        if (_activeLocation != location)
            return;

        if (ownerId.HasValue && _activeLocationOwner != ownerId.Value)
        {
            DiagnosticRecorder.RecordTradeStateReset(
                location,
                "superseded_owner_exit",
                changed: false);
            return;
        }

        int clearedSessionCount = Sessions.Count;
        foreach (TradeSessionSnapshot session in Sessions.Values.ToList())
            EndSession(session, TradeSessionStatus.Canceled);
        AvailablePlayers.Clear();
        AvailableGold.Clear();
        Sessions.Clear();
        SessionByPlayer.Clear();
        TradeStateStore.Reset();
        _activeLocation = null;
        _activeLocationOwner = null;
        if (location == TradeLocation.RestSite)
        {
            TradeRestSiteFlow.Reset();
            AssistSmithCoordinator.Reset();
        }
        DiagnosticRecorder.RecordTradeStateReset(location, "cleanup", clearedSessionCount);
    }

    internal static void Reset()
    {
        _activeLocation = null;
        _activeLocationOwner = null;
        AvailablePlayers.Clear();
        AvailableGold.Clear();
        Sessions.Clear();
        SessionByPlayer.Clear();
        TradeStateStore.Reset();
        TradeTransactionApplier.Reset();
        TradeUsageTracker.Reset();
        TradeRestSiteFlow.Reset();
        AssistSmithCoordinator.Reset();
        DiagnosticRecorder.RecordTradeStateReset(null, "cleanup");
    }

    private static async Task Commit(TradeSessionSnapshot session)
    {
        if (session.Status != TradeSessionStatus.Active)
            return;
        if (session.LockedRevisionA != session.OfferRevisionA ||
            session.LockedRevisionB != session.OfferRevisionB)
        {
            Error(session.PlayerA, ModText.Token(TextKey.OfferChanged));
            Error(session.PlayerB, ModText.Token(TextKey.OfferChanged));
            return;
        }

        Player? playerA = RunManager.Instance.State?.GetPlayer(session.PlayerA);
        Player? playerB = RunManager.Instance.State?.GetPlayer(session.PlayerB);
        if (playerA is null || playerB is null)
        {
            string missingPlayerError = ModText.Token(TextKey.TradePlayerLeft);
            Error(session.PlayerA, missingPlayerError);
            Error(session.PlayerB, missingPlayerError);
            EndSession(session, TradeSessionStatus.Canceled);
            return;
        }
        if (session.Location == TradeLocation.RestSite &&
            (TradeUsageTracker.HasUsed(session.PlayerA) || TradeUsageTracker.HasUsed(session.PlayerB)))
        {
            string alreadyUsedError = ModText.Token(TextKey.APlayerAlreadyTradedHere);
            Error(session.PlayerA, alreadyUsedError);
            Error(session.PlayerB, alreadyUsedError);
            EndSession(session, TradeSessionStatus.Canceled);
            return;
        }
        if (!TradeValidator.TryResolvePair(
                playerA,
                session.OfferA,
                playerB,
                session.OfferB,
                session.Location,
                session.Location == TradeLocation.Merchant ? session.GoldA : playerA.Gold,
                session.Location == TradeLocation.Merchant ? session.GoldB : playerB.Gold,
                out _,
                out _,
                out string error))
        {
            Error(session.PlayerA, error);
            Error(session.PlayerB, error);
            EndSession(session, TradeSessionStatus.Canceled);
            return;
        }

        session.Status = TradeSessionStatus.Committing;
        DiagnosticRecorder.RecordSessionSnapshot(session, "committing");
        BroadcastSnapshot(session);
        TradeSessionSnapshot committed = session.Clone();
        committed.Status = TradeSessionStatus.Committed;

        if (!await TradeTransactionApplier.Apply(committed))
        {
            Error(session.PlayerA, ModText.Token(TextKey.HostTradeApplyFailed));
            Error(session.PlayerB, ModText.Token(TextKey.HostTradeApplyFailed));
            EndSession(session, TradeSessionStatus.Canceled);
            return;
        }

        TradeStateStore.MarkHostCommit(committed);
        DiagnosticRecorder.RecordSessionSnapshot(committed, "committed");
        TradeNetwork.Broadcast(new CommitEvent { Snapshot = committed }, applyLocally: false);
        RemoveSession(session);
        if (session.Location == TradeLocation.RestSite)
        {
            AvailablePlayers.Remove(session.PlayerA);
            AvailablePlayers.Remove(session.PlayerB);
            AvailableGold.Remove(session.PlayerA);
            AvailableGold.Remove(session.PlayerB);
            TradeNetwork.Broadcast(new AvailabilityEvent
            {
                PlayerId = session.PlayerA,
                Available = false,
                Location = session.Location
            });
            TradeNetwork.Broadcast(new AvailabilityEvent
            {
                PlayerId = session.PlayerB,
                Available = false,
                Location = session.Location
            });
        }
    }

    private static void EndSession(TradeSessionSnapshot session, TradeSessionStatus status)
    {
        session.Status = status;
        DiagnosticRecorder.RecordSessionSnapshot(
            session,
            status switch
            {
                TradeSessionStatus.Pending => "pending",
                TradeSessionStatus.Active => "active",
                TradeSessionStatus.Committing => "committing",
                TradeSessionStatus.Committed => "committed",
                TradeSessionStatus.Canceled => "canceled",
                _ => "unknown"
            });
        BroadcastSnapshot(session);
        RemoveSession(session);
    }

    private static void CancelForPlayer(ulong playerId)
    {
        if (SessionByPlayer.TryGetValue(playerId, out ulong sessionId) &&
            Sessions.TryGetValue(sessionId, out TradeSessionSnapshot? session))
        {
            EndSession(session, TradeSessionStatus.Canceled);
        }
    }

    private static void RemoveSession(TradeSessionSnapshot session)
    {
        Sessions.Remove(session.SessionId);
        SessionByPlayer.Remove(session.PlayerA);
        SessionByPlayer.Remove(session.PlayerB);
    }

    private static bool TryGetActiveParticipant(
        ulong senderId,
        ulong sessionId,
        out TradeSessionSnapshot session)
    {
        if (!Sessions.TryGetValue(sessionId, out TradeSessionSnapshot? candidate) ||
            candidate.Status != TradeSessionStatus.Active ||
            !candidate.Contains(senderId))
        {
            session = null!;
            Error(senderId, ModText.Token(TextKey.TradeSessionEnded));
            return false;
        }
        session = candidate;
        return true;
    }

    private static void BroadcastSnapshot(TradeSessionSnapshot snapshot) =>
        TradeNetwork.Broadcast(new SessionEvent { Snapshot = snapshot.Clone() });

    private static void BroadcastAvailabilitySnapshot()
    {
        foreach ((ulong playerId, TradeLocation location) in AvailablePlayers)
        {
            TradeNetwork.Broadcast(new AvailabilityEvent
            {
                PlayerId = playerId,
                Available = true,
                Location = location
            });
        }
    }

    private static void Error(ulong targetId, string message)
    {
        string safeMessage = string.IsNullOrWhiteSpace(message)
            ? ModText.Token(TextKey.InvalidTradeRequest)
            : message.Trim();
        TradeNetwork.Broadcast(new TradeErrorEvent { TargetId = targetId, Message = safeMessage });
    }

    private static bool IsRunPlayer(ulong playerId) => RunManager.Instance.State?.GetPlayer(playerId) is not null;

    private static bool IsConnected(ulong playerId)
    {
        if (playerId == TradeNetwork.LocalPlayerId)
            return true;
        return RunManager.Instance.NetService is NetHostGameService host &&
               GameApiCompatibility.IsHostPeerConnected(host, playerId);
    }

    private static ulong CreateSessionId()
    {
        Span<byte> bytes = stackalloc byte[8];
        do
        {
            RandomNumberGenerator.Fill(bytes);
        } while (BitConverter.ToUInt64(bytes) == 0 || Sessions.ContainsKey(BitConverter.ToUInt64(bytes)));
        return BitConverter.ToUInt64(bytes);
    }
}

