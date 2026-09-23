using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BaseLib.Config;
using BetterMultiplayer.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using Xunit;

namespace BetterMultiplayer.Tests;

/// <summary>
/// 惊喜模式（金币共享）。
///
/// 这个功能的要求里有一条是"不给任何解释——玩家必须自己去发现"。
/// 所以除了行为，这里还断言"没有说明"本身：没有悬停提示、没有分组标题。
/// 谁哪天顺手补上一句解释，这里会红。
/// </summary>
public sealed class SurpriseModeTests : IDisposable
{
    private static readonly PropertyInfo Property = typeof(BetterMultiplayerConfig)
        .GetProperty(
            nameof(BetterMultiplayerConfig.SurpriseSharedGold),
            BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("SurpriseSharedGold is missing from BetterMultiplayerConfig.");

    // 配置属性是 static 的，测试之间会互相污染（AssemblyInfo 已关并行）。
    public SurpriseModeTests()
    {
        Reset();
        SharedGoldSync.Reset();
    }

    public void Dispose()
    {
        Reset();
        SharedGoldSync.Reset();
    }

    private static void Reset() => BetterMultiplayerConfig.SurpriseSharedGold = false;

    /// <summary>默认关。没开就等于原版，不会有人"不知不觉"进了这个模式。</summary>
    [Fact]
    public void DefaultsToOff()
    {
        Assert.False(BetterMultiplayerConfig.SurpriseSharedGold);
    }

    /// <summary>
    /// ★ 不给任何解释：没有悬停提示、也没有分组标题。
    /// 分组名本身就是一种提示，所以两个都不能有。
    /// </summary>
    [Fact]
    public void CarriesNoExplanation()
    {
        Assert.Null(Property.GetCustomAttribute<ConfigHoverTipAttribute>());
        Assert.Null(Property.GetCustomAttribute<ConfigSectionAttribute>());
    }

    /// <summary>
    /// 属性必须是静态的。BaseLib 的 CheckConfigProperties 只收集静态属性，
    /// 实例属性会被静默忽略——设置页上根本不会出现这一行。
    /// </summary>
    [Fact]
    public void IsStatic()
    {
        Assert.True(Property.GetMethod?.IsStatic);
        Assert.True(Property.SetMethod?.IsStatic);
    }

    /// <summary>
    /// 它前面不能有带分组的属性——否则它会被塞进那个分组里。
    ///
    /// BaseLib 的 SectionTracker 碰到"没有分组的属性"时只是不新建分组，
    /// 行会被挂到【当前容器】。所以只要它之前有任何一个属性开了分组，
    /// 这一行就不再是设置页顶部一个孤零零的开关了。
    ///
    /// 注意：这里和 BaseLib 的排版本身都依赖"反射返回声明顺序"。
    /// 单个类上 .NET 实际就是这个顺序，测试和框架的假设是一致的。
    /// </summary>
    [Fact]
    public void NothingBeforeItOpensASection()
    {
        PropertyInfo[] declared = typeof(BetterMultiplayerConfig)
            .GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

        int surpriseIndex = Array.IndexOf(declared, Property);
        Assert.True(surpriseIndex >= 0, "SurpriseSharedGold 不在声明列表里。");

        for (int i = 0; i < surpriseIndex; i++)
        {
            Assert.Null(
                declared[i].GetCustomAttribute<ConfigSectionAttribute>());
        }
    }

    /// <summary>
    /// ★ 回归：惊喜模式曾经"联机进去卡死"。
    ///
    /// 根因是 SharedGoldSync 挂在 Player.Gold 的 setter 上，而金币在关卡加载、
    /// 玩家初始化时就会被赋值——那一刻网络还没就绪，裸读 NetService 会抛异常。
    /// 异常从 setter 里冒出去，就卡住了游戏的加载流程。
    ///
    /// 这几个条件缺一不可，其中 gameLoading 正是当时漏掉的那个。
    /// 谁把它删了，这里会红。
    /// </summary>
    [Theory]
    [InlineData(false, true, false, 2, false)]  // 不在 run 里（主菜单）
    [InlineData(true, false, false, 2, false)]  // 还没连上
    [InlineData(true, true, true, 2, false)]    // ★ 关卡加载中——卡死就发生在这个窗口
    [InlineData(true, true, false, 1, false)]   // 单人，没什么可共享的
    [InlineData(true, true, false, 0, false)]   // 玩家列表还没建好
    [InlineData(true, true, false, 2, true)]    // 全部满足
    public void ShouldSyncRequiresEveryCondition(
        bool inProgress,
        bool connected,
        bool gameLoading,
        int playerCount,
        bool expected)
    {
        Assert.Equal(
            expected,
            SharedGoldSync.ShouldSync(inProgress, connected, gameLoading, playerCount));
    }

    /// <summary>
    /// ★ 回归：客户端也必须参与同步，不能只等房主广播。
    ///
    /// 锁步模型下两端收到的是同一批金币动作，确定性算法算出的结果必然相同；
    /// 而只让房主算、客户端干等广播，广播一漏两端就分叉
    /// ——游戏会弹「数据不同步」并断开连接。
    ///
    /// 所以 ShouldSync 里**没有** isHost 这个条件。谁把它加回去，这里会红。
    /// </summary>
    [Fact]
    public void ShouldSyncDoesNotDependOnBeingHost()
    {
        Assert.True(SharedGoldSync.ShouldSync(
            inProgress: true,
            connected: true,
            gameLoading: false,
            playerCount: 2));
    }

    /// <summary>
    /// ★ 回归：读"是不是房主"不能因为网络还没就绪就抛异常。
    ///
    /// 这里显式把 NetService 拿掉，复现关卡加载早期"还没就绪"的状态。
    /// 修复前这一句是裸访问，会抛 NullReferenceException——而它是在
    /// Player.Gold 的 setter 里被调用的，异常冒出去就是游戏加载卡死。
    /// </summary>
    [Fact]
    public void ReadingHostFlagSurvivesAMissingNetService()
    {
        RunManager manager = RunManager.Instance;

        PropertyInfo netServiceProperty = typeof(RunManager).GetProperty(
            "NetService",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(RunManager).FullName, "NetService");

        object? saved = netServiceProperty.GetValue(manager);
        try
        {
            netServiceProperty.SetValue(manager, null);

            Exception? thrown = Record.Exception(() => _ = TradeNetwork.IsHost);

            Assert.Null(thrown);
        }
        finally
        {
            netServiceProperty.SetValue(manager, saved);
        }
    }

    /// <summary>
    /// 回归：首次播种必须有真实的 RunManager.Launch 接线。
    /// 仅保留初始化 helper、但删掉 Harmony Postfix 调用，功能测试仍可能全绿；
    /// 这个 production-bound 哨兵会在该接线被删除时失败。
    /// </summary>
    [Fact]
    public void RunLaunchPatchCallsTheInitialSyncEntryPoint()
    {
        MethodInfo postfix = typeof(SharedGoldRunLaunchPatch).GetMethod(
            "Postfix",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(SharedGoldRunLaunchPatch).FullName, "Postfix");
        MethodInfo schedule = typeof(SharedGoldSync).GetMethod(
            nameof(SharedGoldSync.ScheduleInitialSync),
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingMethodException(typeof(SharedGoldSync).FullName, nameof(SharedGoldSync.ScheduleInitialSync));

        Assert.NotNull(typeof(SharedGoldRunLaunchPatch).GetCustomAttribute<HarmonyPatch>());
        Assert.Contains(
            ReadCallTokens(postfix),
            token => token == schedule.MetadataToken);
    }

    /// <summary>
    /// 回归：如果加载完成后没有任何新的 Player.Gold setter，
    /// 首次同步仍要把完整玩家列表的总和写回每个人。
    /// </summary>
    [Fact]
    public void InitialPoolSeedsAfterLoadingWithoutAnotherGoldSetter()
    {
        BetterMultiplayerConfig.SurpriseSharedGold = true;
        RunState state = CreateRunState(100, 50);
        using RunManagerScope scope = new(state);

        Assert.True(SharedGoldSync.InitializeInitialPool(state));
        Assert.Equal(new[] { 150, 150 }, state.Players.Select(player => player.Gold));
        Assert.False(SharedGoldSync.InitializeInitialPool(state));
    }

    /// <summary>
    /// ★ 开局播种必须是**求和**，不能照抄某一个人的值。
    ///
    /// A=100、B=50 时如果照抄触发者的值：A 捡 10 会让全员变 110，
    /// B 那 50 凭空消失。求和才是 100+50+10 = 160。
    ///
    /// 这条守的就是"全队总和"这个语义——谁把它改回照抄，这里会红。
    /// </summary>
    [Fact]
    public void SeedPoolAddsUpEveryPlayersGold()
    {
        RunState state = CreateRunState(100, 50);

        Assert.Equal(150, SharedGoldSync.SeedPool(state));
    }

    /// <summary>人数和取值都不该影响求和本身。</summary>
    [Fact]
    public void SeedPoolHandlesAnyPlayerCount()
    {
        Assert.Equal(0, SharedGoldSync.SeedPool(CreateRunState()));
        Assert.Equal(7, SharedGoldSync.SeedPool(CreateRunState(7)));
        Assert.Equal(325, SharedGoldSync.SeedPool(CreateRunState(200, 100, 25)));
    }

    /// <summary>
    /// ★ 第一次同步走"求和"，不看触发者的值。
    ///
    /// A=100、B=50，A 捡 10 变 110 —— 池子该是 160（100+50+10），不是 110。
    /// 这条和下面那条一起，守住"开局求和、之后跟随触发者"这个完整决策。
    /// </summary>
    [Fact]
    public void FirstSyncSeedsFromTheTotalNotTheTrigger()
    {
        RunState state = CreateRunState(110, 50);

        Assert.Equal(160, SharedGoldSync.NextPool(seeded: false, triggeringGold: 110, state));
    }

    /// <summary>
    /// 播种之后跟随触发者：捡钱池子变大、花钱池子变小。
    ///
    /// 因为上一次同步已经把全员设成池子，"触发者的新值"就等于新池子。
    /// </summary>
    [Fact]
    public void LaterSyncsFollowTheTriggeringPlayer()
    {
        RunState state = CreateRunState(160, 160);

        // 捡到 10 → 触发者变 170
        Assert.Equal(170, SharedGoldSync.NextPool(seeded: true, triggeringGold: 170, state));

        // 花掉 30 → 触发者变 140
        Assert.Equal(140, SharedGoldSync.NextPool(seeded: true, triggeringGold: 140, state));
    }

    /// <summary>
    /// ★★ 回归：退出重进后金币按人数翻倍（四人各 60 → 240 → 960 → 3840）。
    ///
    /// 根因是"求和"**不是幂等操作**：合并过一次之后，从当前状态分辨不出
    /// "没合并过的原始值"和"合并过的结果"（两种情况都是全员金币相同），
    /// 而静态字段活不过进程重启，于是重进时会再求一次和。
    ///
    /// 修法：读档进局沿用现有值，只有新开一局才求和。
    /// </summary>
    [Fact]
    public void RestoredRunKeepsTheExistingPoolInsteadOfSummingAgain()
    {
        // 四人各 240 —— 这是上一次合并的结果，存档里存的就是这个值。
        RunState state = CreateRunState(240, 240, 240, 240);

        Assert.Equal(240, SharedGoldSync.NextPool(
            seeded: false,
            triggeringGold: 0,
            state,
            restoredFromSave: true));
    }

    /// <summary>新开一局仍然要合并全队的钱。</summary>
    [Fact]
    public void FreshRunStillMergesEveryonesGold()
    {
        RunState state = CreateRunState(60, 60, 60, 60);

        Assert.Equal(240, SharedGoldSync.NextPool(
            seeded: false,
            triggeringGold: 0,
            state,
            restoredFromSave: false));
    }

    /// <summary>读档池子取最大值——任何情况下都不会把数字放大。</summary>
    [Fact]
    public void ExistingPoolTakesTheLargestBalance()
    {
        Assert.Equal(240, SharedGoldSync.ExistingPool(CreateRunState(240, 240, 240, 240)));
        Assert.Equal(70, SharedGoldSync.ExistingPool(CreateRunState(70, 60, 50)));
        Assert.Equal(0, SharedGoldSync.ExistingPool(CreateRunState()));
    }

    /// <summary>
    /// ★ 核心不变式：读档 → 沿用 → 再读档，值必须稳定。
    /// 这条直接模拟"退出重进"循环——修好之前它会 240 → 960 → 3840。
    /// </summary>
    [Fact]
    public void RepeatedRestoresDoNotInflateThePool()
    {
        RunState state = CreateRunState(240, 240, 240, 240);

        Assert.Equal(240, SharedGoldSync.NextPool(false, 0, state, restoredFromSave: true));
        Assert.Equal(240, SharedGoldSync.NextPool(false, 0, state, restoredFromSave: true));
        Assert.Equal(240, SharedGoldSync.NextPool(false, 0, state, restoredFromSave: true));
    }

    /// <summary>
    /// 造一个只有金币、没别的东西的 RunState。
    ///
    /// 和 TradeTwoClientSimulationTests 用的是同一套反射手法：RunState 和 Player
    /// 都没有能直接 new 出来的构造，只能先拿未初始化对象再塞字段。
    /// </summary>
    private static RunState CreateRunState(params int[] golds)
    {
        const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        List<Player> players = [];
        foreach (int gold in golds)
        {
            Player player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            player.Gold = gold;
            players.Add(player);
        }

        RunState state = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        FieldInfo field = typeof(RunState).GetField("_players", InstanceFlags)
            ?? throw new MissingFieldException(typeof(RunState).FullName, "_players");
        field.SetValue(state, players);
        return state;
    }

    private static IEnumerable<int> ReadCallTokens(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        Dictionary<short, OpCode> opCodes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);

        int offset = 0;
        while (offset < il.Length)
        {
            short value = il[offset++] == 0xfe
                ? unchecked((short)(0xfe00 | il[offset++]))
                : il[offset - 1];
            OpCode opCode = opCodes[value];
            int operandOffset = offset;
            int operandSize = OperandSize(opCode.OperandType, il, operandOffset);
            if ((opCode == OpCodes.Call || opCode == OpCodes.Callvirt) &&
                opCode.OperandType == OperandType.InlineMethod)
                yield return BitConverter.ToInt32(il, operandOffset);
            offset += operandSize;
        }
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

    private sealed class RunManagerScope : IDisposable
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly RunManager _manager = RunManager.Instance;
        private readonly PropertyInfo _stateProperty;
        private readonly PropertyInfo _netServiceProperty;
        private readonly object? _previousState;
        private readonly object? _previousNetService;

        internal RunManagerScope(RunState state)
        {
            _stateProperty = typeof(RunManager).GetProperty("State", InstanceFlags)
                ?? throw new MissingMemberException(typeof(RunManager).FullName, "State");
            _netServiceProperty = typeof(RunManager).GetProperty("NetService", InstanceFlags)
                ?? throw new MissingMemberException(typeof(RunManager).FullName, "NetService");
            _previousState = _stateProperty.GetValue(_manager);
            _previousNetService = _netServiceProperty.GetValue(_manager);

            INetGameService service = DispatchProxy.Create<INetGameService, HostNetServiceProxy>();
            _stateProperty.SetValue(_manager, state);
            _netServiceProperty.SetValue(_manager, service);
        }

        public void Dispose()
        {
            _stateProperty.SetValue(_manager, _previousState);
            _netServiceProperty.SetValue(_manager, _previousNetService);
        }
    }

    private class HostNetServiceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            return targetMethod.Name switch
            {
                "get_Type" => NetGameType.Host,
                "get_IsConnected" => true,
                "get_IsGameLoading" => false,
                "get_NetId" => 1UL,
                "SendMessage" or "SetGameLoading" or "SetBufferMessages" or
                    "RegisterMessageHandler" or "UnregisterMessageHandler" or
                    "add_Disconnected" or "remove_Disconnected" => null,
                _ when targetMethod.ReturnType == typeof(void) => null,
                _ when targetMethod.ReturnType.IsValueType => Activator.CreateInstance(targetMethod.ReturnType),
                _ => null
            };
        }
    }
}
