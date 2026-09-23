using System.Reflection;
using BetterMultiplayer.Trading.Messages;

namespace BetterMultiplayer.Tests;

[CollectionDefinition("AssistSmithTwoClientState", DisableParallelization = true)]
public sealed class AssistSmithTwoClientStateCollection;

[Collection("AssistSmithTwoClientState")]
public sealed class AssistSmithTwoClientSimulationTests : IDisposable
{
    private const ulong ClientPlayerId = 202;
    private static readonly TimeSpan CompletionBound = TimeSpan.FromMilliseconds(250);

    public AssistSmithTwoClientSimulationTests() => TradeCoordinator.Reset();

    public void Dispose() => TradeCoordinator.Reset();

    [Fact]
    public async Task CancelRequestDeliveredBeforeHostRegistrationIsReplayedToTheWaitingClient()
    {
        DeterministicAssistSmithLink link = new(ClientPlayerId);
        Task<AssistSmithResult> waiting = link.Client.BeginAttempt();

        link.DeliverRequestToHost(new AssistSmithRequest
        {
            Canceled = true,
            CardIndex = -1
        });

        Assert.False(waiting.IsCompleted);

        link.RegisterHostSelection();

        Task completed = await Task.WhenAny(waiting, Task.Delay(CompletionBound));
        Assert.Same(waiting, completed);
        AssistSmithResult result = await waiting;
        Assert.Equal(1, link.BroadcastCount);
        Assert.False(result.Success);
        Assert.Equal(-1, result.CardIndex);
    }

    [Fact]
    public void PendingRequestFromDisconnectedClientIsNotReplayedWhenSamePlayerRegistersAgain()
    {
        DeterministicAssistSmithLink link = new(ClientPlayerId);

        link.DeliverRequestToHost(new AssistSmithRequest
        {
            Canceled = true,
            CardIndex = -1
        });
        link.DisconnectClient();
        link.RegisterHostSelection();

        Assert.Equal(0, link.BroadcastCount);
    }

    [Fact]
    public async Task EndingRestSiteCancelsOutstandingAssistSmithAttempt()
    {
        TradeCoordinator.BeginLocation(TradeLocation.RestSite);
        Task<AssistSmithResult> waiting = AssistSmithFlow.WaitForResult(ClientPlayerId);

        TradeCoordinator.EndLocation(TradeLocation.RestSite);

        Task completed = await Task.WhenAny(waiting, Task.Delay(CompletionBound));
        Assert.Same(waiting, completed);
        AssistSmithResult result = await waiting;
        Assert.False(result.Success);
        Assert.Equal(-1, result.CardIndex);
    }

    [Fact]
    public void BeginningRestSiteDiscardsPendingResultFromPreviousVisit()
    {
        AssistSmithFlow.Complete(
            ClientPlayerId,
            new AssistSmithResult(true, 303, 4, "Card:Strike", 0, string.Empty));

        TradeCoordinator.BeginLocation(TradeLocation.RestSite);
        Task<AssistSmithResult> nextAttempt = AssistSmithFlow.WaitForResult(ClientPlayerId);

        Assert.False(nextAttempt.IsCompleted);
    }

    private sealed class DeterministicAssistSmithLink
    {
        private readonly HostEndpoint _host;

        internal DeterministicAssistSmithLink(ulong clientPlayerId)
        {
            Client = new ClientEndpoint(clientPlayerId);
            _host = new HostEndpoint(Broadcast);
        }

        internal ClientEndpoint Client { get; }
        internal int BroadcastCount { get; private set; }

        internal void DeliverRequestToHost(AssistSmithRequest request) =>
            _host.Receive(Client.PlayerId, request);

        internal void DisconnectClient() => _host.Disconnect(Client.PlayerId);

        internal void RegisterHostSelection() => _host.Register(Client.PlayerId);

        private void Broadcast(ulong playerId, AssistSmithResult result)
        {
            BroadcastCount++;
            if (playerId == Client.PlayerId)
                Client.Receive(result);
        }
    }

    private sealed class ClientEndpoint(ulong playerId)
    {
        internal ulong PlayerId { get; } = playerId;

        internal Task<AssistSmithResult> BeginAttempt() =>
            AssistSmithFlow.WaitForResult(PlayerId);

        internal void Receive(AssistSmithResult result) =>
            AssistSmithFlow.Complete(PlayerId, result);
    }

    private sealed class HostEndpoint(Action<ulong, AssistSmithResult> broadcast)
    {
        private static readonly Type BroadcastType = typeof(Action<ulong, AssistSmithResult>);
        private static readonly MethodInfo? ResolveWithBroadcast = FindCoordinatorMethod(
            nameof(AssistSmithCoordinator.Resolve),
            typeof(ulong),
            typeof(bool),
            typeof(ulong),
            typeof(int),
            typeof(string),
            typeof(int),
            BroadcastType);
        private static readonly MethodInfo? RegisterWithBroadcast = FindCoordinatorMethod(
            nameof(AssistSmithCoordinator.Register),
            typeof(ulong),
            BroadcastType);
        private static readonly MethodInfo? DisconnectWithBroadcast = FindCoordinatorMethod(
            nameof(AssistSmithCoordinator.PlayerDisconnected),
            typeof(ulong),
            BroadcastType);

        internal void Receive(ulong senderId, AssistSmithRequest request)
        {
            if (ResolveWithBroadcast is not null && RegisterWithBroadcast is not null)
            {
                ResolveWithBroadcast.Invoke(
                    null,
                    [
                        senderId,
                        request.Canceled,
                        request.TargetId,
                        request.CardIndex,
                        request.CardId,
                        request.UpgradeLevel,
                        broadcast
                    ]);
                return;
            }

            AssistSmithCoordinator.Resolve(
                senderId,
                request.Canceled,
                request.TargetId,
                request.CardIndex,
                request.CardId,
                request.UpgradeLevel);
        }

        internal void Disconnect(ulong playerId)
        {
            if (DisconnectWithBroadcast is not null)
                DisconnectWithBroadcast.Invoke(null, [playerId, broadcast]);
        }

        internal void Register(ulong playerId)
        {
            if (ResolveWithBroadcast is not null && RegisterWithBroadcast is not null)
            {
                RegisterWithBroadcast.Invoke(null, [playerId, broadcast]);
                return;
            }

            FieldInfo? activePlayersField = typeof(AssistSmithCoordinator).GetField(
                "ActivePlayers",
                BindingFlags.Static | BindingFlags.NonPublic);
            HashSet<ulong>? activePlayers = activePlayersField?.GetValue(null) as HashSet<ulong>;
            Assert.NotNull(activePlayers);
            activePlayers.Add(playerId);
        }

        private static MethodInfo? FindCoordinatorMethod(string name, params Type[] parameterTypes) =>
            typeof(AssistSmithCoordinator).GetMethod(
                name,
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                types: parameterTypes,
                modifiers: null);
    }
}
