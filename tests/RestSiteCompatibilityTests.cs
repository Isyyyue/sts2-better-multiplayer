using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace BetterMultiplayer.Tests;

[Collection("TradeState")]
public sealed class RestSiteCompatibilityTests : IDisposable
{
    public RestSiteCompatibilityTests() => TradeCoordinator.Reset();

    public void Dispose() => TradeCoordinator.Reset();

    [Fact]
    public void EndPatchTargetsTheCrossBranchRoomExitMethod()
    {
        MethodInfo resolver = Assert.IsAssignableFrom<MethodInfo>(typeof(TradeRestSiteEndPatch).GetMethod(
            "TargetMethod",
            BindingFlags.Static | BindingFlags.NonPublic));
        MethodBase target = Assert.IsAssignableFrom<MethodBase>(resolver.Invoke(null, null));

        Assert.Equal(typeof(RestSiteRoom), target.DeclaringType);
        Assert.Equal(nameof(RestSiteRoom.Exit), target.Name);
        ParameterInfo parameter = Assert.Single(target.GetParameters());
        Assert.Equal(typeof(IRunState), parameter.ParameterType);
        Assert.Equal(typeof(Task), Assert.IsAssignableFrom<MethodInfo>(target).ReturnType);

        MethodInfo prefix = Assert.IsAssignableFrom<MethodInfo>(typeof(TradeRestSiteEndPatch).GetMethod(
            "Prefix",
            BindingFlags.Static | BindingFlags.NonPublic));
        Assert.NotNull(prefix.GetCustomAttribute<HarmonyPrefix>());
        Assert.Null(typeof(TradeRestSiteEndPatch).GetMethod(
            "Postfix",
            BindingFlags.Static | BindingFlags.NonPublic));
    }

    [Fact]
    public async Task EndPrefixClearsRestSiteLifecycleAndPendingFlowsIdempotently()
    {
        TradeCoordinator.BeginLocation(TradeLocation.RestSite);
        Assert.True(TradeStateStore.SetAvailability(12, true, TradeLocation.RestSite));
        Task<bool> tradeWaiter = TradeRestSiteFlow.WaitForResult(12);
        Task<AssistSmithResult> smithWaiter = AssistSmithFlow.WaitForResult(12);

        InvokeEndPrefix();
        InvokeEndPrefix();

        Assert.True(tradeWaiter.IsCompletedSuccessfully);
        Assert.False(await tradeWaiter);
        Assert.True(smithWaiter.IsCompletedSuccessfully);
        Assert.False((await smithWaiter).Success);
        Assert.False(TradeStateStore.IsAvailable(12, TradeLocation.RestSite));
        TradeCoordinator.SetAvailable(12, true, TradeLocation.RestSite, reportedGold: 0);
        Assert.False(TradeStateStore.IsAvailable(12, TradeLocation.RestSite));

        TradeCoordinator.BeginLocation(TradeLocation.RestSite);
        Assert.True(TradeStateStore.SetAvailability(12, true, TradeLocation.RestSite));
    }

    [Fact]
    public void HarmonyRegistersTheEndPrefixOnTheResolvedTarget()
    {
        const string harmonyId = "BetterMultiplayer.Tests.RestSiteCompatibility";
        Harmony harmony = new(harmonyId);
        MethodBase target = InvokeTargetMethod();
        try
        {
            harmony.CreateClassProcessor(typeof(TradeRestSiteEndPatch)).Patch();

            Patches patches = Assert.IsType<Patches>(Harmony.GetPatchInfo(target));
            Assert.Contains(patches.Prefixes, patch =>
                patch.owner == harmonyId &&
                patch.PatchMethod.DeclaringType == typeof(TradeRestSiteEndPatch) &&
                patch.PatchMethod.Name == "Prefix");
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    [Fact]
    public void EveryHarmonyPatchClassResolvesAndRegisters()
    {
        const string harmonyId = "BetterMultiplayer.Tests.AllHarmonyTargets";
        Harmony harmony = new(harmonyId);
        Type[] patchTypes = typeof(BetterMultiplayerMod).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes<HarmonyPatch>().Any())
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        // 故意的哨兵数字：删掉一个补丁类时这里会立刻报警。
        // 新增补丁（BetterMultiplayerSettingsEntry、SharedGoldSync）必须同步改这个数，
        // 改动会出现在 diff 里，等于强制"看一眼自己动了什么"。
        Assert.Equal(19, patchTypes.Length);
        try
        {
            foreach (Type patchType in patchTypes)
            {
                Exception? failure = Record.Exception(() =>
                    harmony.CreateClassProcessor(patchType).Patch());
                Assert.True(
                    failure is null,
                    $"Harmony target failed for {patchType.FullName}: {failure}");
            }
        }
        finally
        {
            harmony.UnpatchAll(harmonyId);
        }
    }

    private static void InvokeEndPrefix()
    {
        MethodInfo prefix = Assert.IsAssignableFrom<MethodInfo>(typeof(TradeRestSiteEndPatch).GetMethod(
            "Prefix",
            BindingFlags.Static | BindingFlags.NonPublic));
        prefix.Invoke(null, null);
    }

    private static MethodBase InvokeTargetMethod()
    {
        MethodInfo resolver = Assert.IsAssignableFrom<MethodInfo>(typeof(TradeRestSiteEndPatch).GetMethod(
            "TargetMethod",
            BindingFlags.Static | BindingFlags.NonPublic));
        return Assert.IsAssignableFrom<MethodBase>(resolver.Invoke(null, null));
    }
}
