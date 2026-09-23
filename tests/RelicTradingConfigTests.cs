using System.Reflection;
using BetterMultiplayer.Config;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using Xunit;

namespace BetterMultiplayer.Tests;

/// <summary>
/// 遗物交易设置的判定逻辑。
///
/// 模型很简单，只有两态（设置页上也只有总开关一个分项）：
///
///   效果干净的遗物 —— 一律能交易，不需要任何设置。
///     它们被原版挡住纯粹是因为稀有度是 起始 / 事件 / 先古
///     （原版 IsTradable 的最后一条只看稀有度，和效果无关），
///     交易起来没有任何副作用。**这是本模组唯一一处默认偏离原版的地方。**
///
///   带副作用的遗物 —— 默认禁止，总开关打开后放行。
///     会重复触发获得时效果 / 带状态（已用尽·已融化）/ 附带宠物，共 62 个。
///
/// 用到的样本（原版都禁止交易，所以 Assert.False(IsTradable) 能证明前提成立）：
///   干净：BurningBlood(起始) / BingBong(事件) / BlackStar(先古)
///   有副作用：Byrdpip / PaelsLegion / OldCoin / GoldenPearl / LizardTail
/// </summary>
public sealed class RelicTradingConfigTests : IDisposable
{
    public RelicTradingConfigTests() => ResetAll();

    /// <summary>
    /// 配置属性是 static 的（BaseLib 只认静态属性），所以测试之间会互相污染。
    /// 构造和 Dispose 都复位，配合 AssemblyInfo 里关掉并行，
    /// 保证任何测试开始和结束时看到的都是默认值。
    /// </summary>
    public void Dispose() => ResetAll();

    private static void ResetAll() => BetterMultiplayerConfig.UnlockRelicTrading = false;

    private static LizardTail UsedUpLizardTail()
    {
        // IsUsedUp 是运行时状态。全新实例 _wasUsed 是 false（原版本来就允许交易），
        // 只有用过之后才被挡住。而 new LizardTail() 拿到的是 canonical 实例，
        // 属性 setter 会抛 CanonicalModelException，所以直接写底层字段。
        LizardTail tail = new();
        typeof(LizardTail)
            .GetField("_wasUsed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(tail, true);
        return tail;
    }

    // ------------------------------------------------------------------
    // 效果干净的遗物：默认就能交易
    // ------------------------------------------------------------------

    /// <summary>
    /// 那 101 个效果干净、只因稀有度被挡的遗物，现在直接当正常遗物处理——
    /// 总开关关着也能交易。
    /// </summary>
    [Theory]
    [InlineData("starter")]
    [InlineData("event")]
    [InlineData("ancient")]
    public void CleanRarityBlockedRelicsAreTradableByDefault(string tier)
    {
        ResetAll();

        RelicModel relic = tier switch
        {
            "starter" => new BurningBlood(),
            "event" => new BingBong(),
            _ => new BlackStar()
        };

        // 前提：它确实被原版挡住了，否则这个测试没意义。
        Assert.False(relic.IsTradable);

        Assert.True(BetterMultiplayerConfig.CanTrade(relic));
        Assert.True(TradeValidator.CanTradeRelic(relic));
    }

    // ------------------------------------------------------------------
    // 带副作用的遗物：默认禁止，总开关放行
    // ------------------------------------------------------------------

    /// <summary>带副作用的遗物默认全部禁止，打开总开关后全部放行。</summary>
    [Fact]
    public void SideEffectRelicsAreBlockedUntilTheSwitchIsOn()
    {
        ResetAll();

        RelicModel[] withSideEffects =
        [
            new Byrdpip(),      // 附带宠物 + 拾取时生效
            new PaelsLegion(),  // 只设了 AddsPet
            new OldCoin(),      // 拾取时生效
            new GoldenPearl()   // 拾取时生效
        ];

        foreach (RelicModel relic in withSideEffects)
        {
            Assert.False(
                BetterMultiplayerConfig.CanTrade(relic),
                $"{relic.GetType().Name} 在总开关关着时不该放行。");
        }

        BetterMultiplayerConfig.UnlockRelicTrading = true;

        foreach (RelicModel relic in withSideEffects)
        {
            Assert.True(
                BetterMultiplayerConfig.CanTrade(relic),
                $"{relic.GetType().Name} 在总开关打开后应该放行。");
        }
    }

    /// <summary>「已用尽」类（蜥蜴尾用过之后）同样由总开关决定。</summary>
    [Fact]
    public void UsedUpRelicsAreBlockedUntilTheSwitchIsOn()
    {
        ResetAll();

        LizardTail used = UsedUpLizardTail();
        Assert.True(used.IsUsedUp);
        Assert.False(used.IsTradable);
        Assert.False(BetterMultiplayerConfig.CanTrade(used));

        BetterMultiplayerConfig.UnlockRelicTrading = true;
        Assert.True(BetterMultiplayerConfig.CanTrade(used));
    }

    /// <summary>
    /// ★ 回归测试：「佩尔的士兵」只设了 AddsPet，没设 SpawnsPets。
    /// 原版 IsTradable 只查 SpawnsPets，它靠 Ancient 稀有度才被挡住。
    /// 如果副作用判定漏了 AddsPet，它会被判成"效果干净"而【默认放行】，
    /// AfterObtained 再次 AddPet 造成宠物重复。
    /// </summary>
    [Fact]
    public void PaelsLegionIsNotMistakenForACleanRelic()
    {
        ResetAll();

        RelicModel legion = new PaelsLegion();
        Assert.False(legion.SpawnsPets);
        Assert.True(legion.AddsPet);

        Assert.False(BetterMultiplayerConfig.CanTrade(legion));
    }

    // ------------------------------------------------------------------
    // 不变量
    // ------------------------------------------------------------------

    /// <summary>原版本来就能交易的遗物，任何设置下都不会被禁止。</summary>
    [Fact]
    public void VanillaTradableRelicsAreNeverRestricted()
    {
        RelicModel[] tradable = [new Anchor(), new BloodVial(), new BagOfMarbles(), new BeltBuckle()];

        foreach (RelicModel relic in tradable)
        {
            ResetAll();
            Assert.True(TradeValidator.CanTradeRelic(relic));

            BetterMultiplayerConfig.UnlockRelicTrading = true;
            Assert.True(TradeValidator.CanTradeRelic(relic));
        }
    }

    /// <summary>
    /// 三态对照：总开关关着时，能交易的正好是"原版允许 + 效果干净"两类，
    /// 带副作用的那一类被挡住。
    /// </summary>
    [Fact]
    public void MasterSwitchOffOnlyBlocksSideEffectRelics()
    {
        ResetAll();

        Assert.True(BetterMultiplayerConfig.CanTrade(new Anchor()));      // 原版允许
        Assert.True(BetterMultiplayerConfig.CanTrade(new BlackStar()));   // 原版禁止但效果干净
        Assert.False(BetterMultiplayerConfig.CanTrade(new Byrdpip()));    // 原版禁止且有副作用
    }
}
