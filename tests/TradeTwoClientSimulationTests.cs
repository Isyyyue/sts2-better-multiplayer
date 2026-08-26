using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BaseLib.Abstracts;
using BetterMultiplayer.Diagnostics;
using BetterMultiplayer.Trading;
using BetterMultiplayer.Trading.Messages;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Quality;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace BetterMultiplayer.Tests;

[Collection("TradeState")]
public sealed class TradeTwoClientSimulationTests : IDisposable
{
    private const ulong HostPlayerId = 101;
    private const ulong ClientPlayerId = 202;

    public TradeTwoClientSimulationTests() => TradeCoordinator.Reset();

    public void Dispose() => TradeCoordinator.Reset();

    [Fact]
    public void SupersededMerchantOwnerExitPreservesBothPlayersUntilCurrentOwnerExits()
    {
        const ulong firstRoomOwner = 1001;
        const ulong currentRoomOwner = 2002;
        DeterministicAvailabilityLink link = new();

        BeginMerchant(firstRoomOwner);
        BeginMerchant(currentRoomOwner);
        link.Enqueue(HostPlayerId, available: true);
        link.Enqueue(ClientPlayerId, available: true);

        Assert.Equal(2, link.PendingCount);
        Assert.False(TradeStateStore.IsAvailable(HostPlayerId, TradeLocation.Merchant));
        Assert.False(TradeStateStore.IsAvailable(ClientPlayerId, TradeLocation.Merchant));
        Assert.True(link.DeliverNext());
        Assert.True(link.DeliverNext());
        Assert.Equal(0, link.PendingCount);

        EndMerchant(firstRoomOwner);

        Assert.True(TradeStateStore.IsAvailable(HostPlayerId, TradeLocation.Merchant));
        Assert.True(TradeStateStore.IsAvailable(ClientPlayerId, TradeLocation.Merchant));

        EndMerchant(currentRoomOwner);

        Assert.False(TradeStateStore.IsAvailable(HostPlayerId, TradeLocation.Merchant));
        Assert.False(TradeStateStore.IsAvailable(ClientPlayerId, TradeLocation.Merchant));
    }

    [Fact]
    public void CurrentMerchantOwnerExitClearsImmediatelyBeforeSupersededOwnerExit()
    {
        const ulong supersededRoomOwner = 4004;
        const ulong currentRoomOwner = 5005;
        DeterministicAvailabilityLink link = new();

        BeginMerchant(supersededRoomOwner);
        BeginMerchant(currentRoomOwner);
        link.Enqueue(HostPlayerId, available: true);
        link.Enqueue(ClientPlayerId, available: true);
        Assert.True(link.DeliverNext());
        Assert.True(link.DeliverNext());

        EndMerchant(currentRoomOwner);

        Assert.False(TradeStateStore.IsAvailable(HostPlayerId, TradeLocation.Merchant));
        Assert.False(TradeStateStore.IsAvailable(ClientPlayerId, TradeLocation.Merchant));

        EndMerchant(supersededRoomOwner);

        Assert.False(TradeStateStore.IsAvailable(HostPlayerId, TradeLocation.Merchant));
        Assert.False(TradeStateStore.IsAvailable(ClientPlayerId, TradeLocation.Merchant));
    }

    [Fact]
    public void MerchantPostfixBeginsLifecycleBeforeAnyConditionalGate()
    {
        MethodInfo postfix = typeof(MerchantTradePatch).GetMethod(
            "Postfix",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo lifecycleEntry = typeof(MerchantTradePatch).GetMethod(
            nameof(MerchantTradePatch.TryBeginLifecycle),
            BindingFlags.Static | BindingFlags.NonPublic)!;
        List<IlInstruction> instructions = ReadInstructions(postfix);
        int lifecycleCallIndex = instructions.FindIndex(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.MetadataToken == lifecycleEntry.MetadataToken);

        Assert.True(lifecycleCallIndex >= 0, "Merchant Postfix must call TryBeginLifecycle directly.");
        Assert.DoesNotContain(
            instructions.Take(lifecycleCallIndex),
            instruction => instruction.OpCode.FlowControl == FlowControl.Cond_Branch);
    }

    [Fact]
    public void HostReadyWithOneParticipantAcceptsLaterClientAvailabilityWithoutSecondReady()
    {
        const ulong roomOwner = 3003;
        DeterministicAvailabilityLink link = new();
        LogicalClientView remoteClientView = new();
        using RunManagerSimulationScope run = new(HostPlayerId);

        long sequence = DiagnosticRecorder.Snapshot().LastOrDefault()?.Sequence ?? 0;
        link.Enqueue(ClientPlayerId, available: true);

        Assert.True(link.DeliverNextToCoordinator());
        Assert.False(TradeStateStore.IsAvailable(ClientPlayerId, TradeLocation.Merchant));
        Assert.Contains(
            DiagnosticRecorder.Snapshot(),
            entry => entry.Sequence > sequence &&
                entry.Code == DiagnosticEventCode.TradeAvailabilityHandled &&
                entry.Facts?.Reason == "no_active_location");

        Assert.Equal(1, run.ParticipantCount);
        bool lifecycleEstablished = MerchantTradePatch.TryBeginLifecycle(
            NetGameType.Host,
            roomOwner);
        link.Enqueue(HostPlayerId, available: true);

        Assert.True(lifecycleEstablished);
        Assert.True(link.DeliverNextToCoordinator());
        Assert.True(TradeStateStore.IsAvailable(HostPlayerId, TradeLocation.Merchant));
        Assert.True(run.NetService.PendingAvailabilityCount > 0);
        while (run.NetService.DeliverNextAvailabilityTo(remoteClientView))
        {
        }
        Assert.True(remoteClientView.IsAvailable(HostPlayerId));

        run.AddPlayer(ClientPlayerId);
        link.Enqueue(ClientPlayerId, available: true);

        Assert.Equal(2, run.ParticipantCount);
        Assert.False(TradeStateStore.IsAvailable(ClientPlayerId, TradeLocation.Merchant));
        Assert.False(remoteClientView.IsAvailable(ClientPlayerId));
        Assert.True(link.DeliverNextToCoordinator());
        while (run.NetService.DeliverNextAvailabilityTo(remoteClientView))
        {
        }

        Assert.True(TradeStateStore.IsAvailable(HostPlayerId, TradeLocation.Merchant));
        Assert.True(TradeStateStore.IsAvailable(ClientPlayerId, TradeLocation.Merchant));
        Assert.True(remoteClientView.IsAvailable(HostPlayerId));
        Assert.True(remoteClientView.IsAvailable(ClientPlayerId));
        Assert.True(run.NetService.BroadcastCount > 0);
    }

    private static void BeginMerchant(ulong ownerId) =>
        TradeCoordinator.BeginLocation(TradeLocation.Merchant, ownerId);

    private static void EndMerchant(ulong ownerId) =>
        TradeCoordinator.EndLocation(TradeLocation.Merchant, ownerId);

    private static List<IlInstruction> ReadInstructions(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        Dictionary<short, OpCode> opCodes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
        List<IlInstruction> instructions = [];
        int offset = 0;

        while (offset < il.Length)
        {
            short value = il[offset++] == 0xfe
                ? unchecked((short)(0xfe00 | il[offset++]))
                : il[offset - 1];
            OpCode opCode = opCodes[value];
            int operandOffset = offset;
            int operandSize = OperandSize(opCode.OperandType, il, operandOffset);
            int? metadataToken = opCode.OperandType == OperandType.InlineMethod
                ? BitConverter.ToInt32(il, operandOffset)
                : null;
            instructions.Add(new IlInstruction(opCode, metadataToken));
            offset += operandSize;
        }

        return instructions;
    }

    private static int OperandSize(OperandType operandType, byte[] il, int offset) =>
        operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or
                OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or
                OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or
                OperandType.InlineTok or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
            _ => throw new InvalidOperationException($"Unsupported IL operand type: {operandType}")
        };

    private readonly record struct IlInstruction(OpCode OpCode, int? MetadataToken);

    private sealed class DeterministicAvailabilityLink
    {
        private readonly Queue<(ulong PlayerId, bool Available)> _pending = [];

        internal void Enqueue(ulong playerId, bool available) =>
            _pending.Enqueue((playerId, available));

        internal int PendingCount => _pending.Count;

        internal bool DeliverNext(bool lifecycleEstablished = true)
        {
            if (!_pending.TryDequeue(out (ulong PlayerId, bool Available) message) ||
                !lifecycleEstablished)
                return false;

            TradeStateStore.SetAvailability(
                message.PlayerId,
                message.Available,
                TradeLocation.Merchant);
            return true;
        }

        internal bool DeliverNextToCoordinator()
        {
            if (!_pending.TryDequeue(out (ulong PlayerId, bool Available) message))
                return false;

            TradeCoordinator.SetAvailable(
                message.PlayerId,
                message.Available,
                TradeLocation.Merchant,
                reportedGold: 0);
            return true;
        }
    }

    private sealed class RunManagerSimulationScope : IDisposable
    {
        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly RunManager _manager = RunManager.Instance;
        private readonly PropertyInfo _stateProperty;
        private readonly PropertyInfo _netServiceProperty;
        private readonly object? _previousState;
        private readonly object? _previousNetService;
        private readonly List<Player> _players;

        internal RunManagerSimulationScope(params ulong[] playerIds)
        {
            _stateProperty = typeof(RunManager).GetProperty("State", InstanceFlags) ??
                throw new MissingMemberException(typeof(RunManager).FullName, "State");
            _netServiceProperty = typeof(RunManager).GetProperty("NetService", InstanceFlags) ??
                throw new MissingMemberException(typeof(RunManager).FullName, "NetService");
            _previousState = _stateProperty.GetValue(_manager);
            _previousNetService = _netServiceProperty.GetValue(_manager);

            INetGameService netService = DispatchProxy.Create<INetGameService, RecordingHostNetService>();
            NetService = (RecordingHostNetService)(object)netService;
            NetService.Configure(HostPlayerId);
            _players = playerIds.Select(CreatePlayer).ToList();
            _stateProperty.SetValue(_manager, CreateRunState(_players));
            _netServiceProperty.SetValue(_manager, netService);
        }

        internal RecordingHostNetService NetService { get; }
        internal int ParticipantCount => _players.Count;

        internal void AddPlayer(ulong playerId) => _players.Add(CreatePlayer(playerId));

        public void Dispose()
        {
            _stateProperty.SetValue(_manager, _previousState);
            _netServiceProperty.SetValue(_manager, _previousNetService);
        }

        private static RunState CreateRunState(List<Player> players)
        {
            RunState state = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
            FieldInfo field = typeof(RunState).GetField("_players", InstanceFlags) ??
                throw new MissingFieldException(typeof(RunState).FullName, "_players");
            field.SetValue(state, players);
            return state;
        }

        private static Player CreatePlayer(ulong playerId)
        {
            Player player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            FieldInfo field = typeof(Player).GetField("<NetId>k__BackingField", InstanceFlags) ??
                throw new MissingFieldException(typeof(Player).FullName, "NetId");
            field.SetValue(player, playerId);
            return player;
        }
    }

    public class RecordingHostNetService : DispatchProxy
    {
        private readonly Queue<AvailabilityEvent> _pendingAvailability = [];
        private ulong _netId;

        internal int BroadcastCount { get; private set; }
        internal int PendingAvailabilityCount => _pendingAvailability.Count;

        internal void Configure(ulong netId) => _netId = netId;

        internal bool DeliverNextAvailabilityTo(LogicalClientView view)
        {
            if (!_pendingAvailability.TryDequeue(out AvailabilityEvent? message))
                return false;

            view.Apply(message);
            return true;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            return targetMethod.Name switch
            {
                "get_NetId" => _netId,
                "get_IsConnected" => true,
                "get_IsGameLoading" => false,
                "get_Type" => NetGameType.Host,
                "get_Platform" => default(PlatformType),
                "get_LocalVersion" => Activator.CreateInstance(targetMethod.ReturnType),
                "SendMessage" => Record(args?[0]),
                "GetStatsForPeer" or "GetRawLobbyIdentifier" => null,
                "add_Disconnected" or "remove_Disconnected" or
                    "RegisterMessageHandler" or "UnregisterMessageHandler" or
                    "Update" or "Disconnect" or "SetGameLoading" or
                    "SetBufferMessages" => null,
                _ => throw new MissingMethodException(targetMethod.DeclaringType?.FullName, targetMethod.Name)
            };
        }

        private object? Record(object? message)
        {
            BroadcastCount++;
            if (message is CustomMessageWrapper
                {
                    Message: AvailabilityEvent availability
                })
            {
                _pendingAvailability.Enqueue(new AvailabilityEvent
                {
                    PlayerId = availability.PlayerId,
                    Available = availability.Available,
                    Location = availability.Location
                });
            }

            return null;
        }
    }

    internal sealed class LogicalClientView
    {
        private readonly HashSet<ulong> _availablePlayers = [];

        internal bool IsAvailable(ulong playerId) => _availablePlayers.Contains(playerId);

        internal void Apply(AvailabilityEvent message)
        {
            if (message.Available)
                _availablePlayers.Add(message.PlayerId);
            else
                _availablePlayers.Remove(message.PlayerId);
        }
    }
}
