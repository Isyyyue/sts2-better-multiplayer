using System.Security.Cryptography;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using BetterMultiplayer.Trading.Messages;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Trading;

internal static class TradeCoordinator
{
    private static readonly Dictionary<ulong, TradeLocation> AvailablePlayers = [];
    private static readonly Dictionary<ulong, int> AvailableGold = [];
    private static readonly Dictionary<ulong, TradeSessionSnapshot> Sessions = [];
    private static readonly Dictionary<ulong, ulong> SessionByPlayer = [];

    internal static void SetAvailable(ulong playerId, bool available, TradeLocation location, int reportedGold)
    {
        if (!Enum.IsDefined(location) || !IsRunPlayer(playerId))
            return;

        if (available && location == TradeLocation.Merchant &&
            !TradeGoldBalance.TryValidateOffer(reportedGold, 0, out string goldError))
        {
            Error(playerId, goldError);
            return;
        }

        if (available)
        {
            AvailablePlayers[playerId] = location;
            AvailableGold[playerId] = location == TradeLocation.Merchant ? reportedGold : 0;
        }
        else if (AvailablePlayers.TryGetValue(playerId, out TradeLocation current) && current == location)
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
    }

    internal static void Invite(ulong senderId, ulong targetId)
    {
        if (senderId == targetId ||
            !AvailablePlayers.TryGetValue(senderId, out TradeLocation location) ||
            !AvailablePlayers.TryGetValue(targetId, out TradeLocation targetLocation) ||
            location != targetLocation ||
            !IsConnected(targetId))
        {
            Error(senderId, ModText.Token(TextKey.PartnerNotAtTradeScreen));
            return;
        }

        if (location == TradeLocation.RestSite &&
            (TradeUsageTracker.HasUsed(senderId) || TradeUsageTracker.HasUsed(targetId)))
        {
            Error(senderId, ModText.Token(TextKey.RestSiteTradeAlreadyUsed));
            return;
        }

        if (SessionByPlayer.ContainsKey(senderId) || SessionByPlayer.ContainsKey(targetId))
        {
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
            session.SetGold(senderId, reportedGold);
            AvailableGold[senderId] = reportedGold;
        }

        session.Status = TradeSessionStatus.Active;
        session.Revision++;
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
            availableGold = reportedGold;
            session.SetGold(senderId, reportedGold);
            AvailableGold[senderId] = reportedGold;
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
        session.ConfirmedA = false;
        session.ConfirmedB = false;
        session.Revision++;
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
        if (revision != session.Revision)
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
                session.SetGold(senderId, reportedGold);
                AvailableGold[senderId] = reportedGold;
                session.ConfirmedA = false;
                session.ConfirmedB = false;
                session.Revision++;
                if (confirmed)
                    Error(senderId, ModText.Token(TextKey.GoldBalanceChanged));
                BroadcastSnapshot(session);
                return;
            }
        }

        if (senderId == session.PlayerA)
            session.ConfirmedA = confirmed;
        else
            session.ConfirmedB = confirmed;
        BroadcastSnapshot(session);

        if (session.ConfirmedA && session.ConfirmedB)
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
        TradeNetwork.Broadcast(new AvailabilityEvent
        {
            PlayerId = playerId,
            Available = false,
            Location = location
        });
    }

    internal static void BeginLocation(TradeLocation location)
    {
        foreach (TradeSessionSnapshot session in Sessions.Values.ToList())
            EndSession(session, TradeSessionStatus.Canceled);
        AvailablePlayers.Clear();
        AvailableGold.Clear();
        Sessions.Clear();
        SessionByPlayer.Clear();
        TradeStateStore.Reset();
        if (location == TradeLocation.RestSite)
        {
            TradeUsageTracker.BeginRestSite();
            TradeRestSiteFlow.BeginRestSite();
            AssistSmithCoordinator.BeginRestSite();
        }
    }

    internal static void Reset()
    {
        AvailablePlayers.Clear();
        AvailableGold.Clear();
        Sessions.Clear();
        SessionByPlayer.Clear();
        TradeStateStore.Reset();
        TradeTransactionApplier.Reset();
        TradeUsageTracker.Reset();
        TradeRestSiteFlow.Reset();
        AssistSmithCoordinator.Reset();
    }

    private static async Task Commit(TradeSessionSnapshot session)
    {
        if (session.Status != TradeSessionStatus.Active)
            return;

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
               host.ConnectedPeers.Any(peer => peer.peerId == playerId);
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
