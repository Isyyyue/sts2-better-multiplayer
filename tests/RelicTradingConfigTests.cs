using System.Reflection;
using BetterMultiplayer.Config;
using BetterMultiplayer.Trading;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using Xunit;

namespace BetterMultiplayer.Tests;

/// <summary>
/// 遗物交易设置的判定逻辑。
///
/// 原版 RelicModel.IsTradable 是"任一条件命中即禁止"；本模组把它拆成
/// 逐条可放开，改法是"每一条都必须被对应开关单独放开"。
/// 这里用真实遗物实例验证这个"与"关系。
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

    /// <summary>把全部开关恢复默认（全关）。</summary>
    private static void ResetAll()
    {
        BetterMultiplayerConfig.UnlockRelicTrading = false;
        BetterMultiplayerConfig.AllowStarterRelics = false;
        BetterMultiplayerConfig.AllowEventRelics = false;
        BetterMultiplayerConfig.AllowAncientRelics = false;
        BetterMultiplayerConfig.AllowUsedUpRelics = false;
        BetterMultiplayerConfig.AllowMeltedRelics = false;
        BetterMultiplayerConfig.AllowUponPickupRelics = false;
        BetterMultiplayerConfig.AllowPetRelics = false;
    }

    /// <summary>默认全关时必须等价于原版——不能悄悄放开任何东西。</summary>
    [Fact]
    public void DefaultsDenyEverythingVanillaDenies()
    {
        ResetAll();

        Assert.False(BetterMultiplayerConfig.AllowsRelic(new Byrdpip()));
        Assert.False(BetterMultiplayerConfig.AllowsRelic(new PaelsLegion()));
        Assert.False(BetterMultiplayerConfig.AllowsRelic(new OldCoin()));
        Assert.False(BetterMultiplayerConfig.AllowsRelic(new LizardTail()));
    }

    /// <summary>只开总开关、不开任何分项时，仍然什么都不放开。</summary>
    [Fact]
    public void MasterSwitchAloneUnlocksNothing()
    {
        ResetAll();
        BetterMultiplayerConfig.UnlockRelicTrading = true;

        Assert.False(BetterMultiplayerConfig.AllowsRelic(new Byrdpip()));
        Assert.False(BetterMultiplayerConfig.AllowsRelic(new PaelsLegion()));
        Assert.False(BetterMultiplayerConfig.AllowsRelic(new OldCoin()));
    }

    /// <summary>
    /// 「幼年异鸟」同时是 事件稀有度 + 召唤宠物 + 拾取时生效，
    /// 三个开关必须全开才放行。
    /// </summary>
    [Fact]
    public void ByrdpipNeedsAllThreeSwitches()
    {
        ResetAll();
        BetterMultiplayerConfig.UnlockRelicTrading = true;
        BetterMultiplayerConfig.AllowEventRelics = true;

        Assert.False(BetterMultiplayerConfig.AllowsRelic(new Byrdpip()));

        BetterMultiplayerConfig.AllowPetRelics = true;
        Assert.False(BetterMultiplayerConfig.AllowsRelic(new Byrdpip()));

        BetterMultiplayerConfig.AllowUponPickupRelics = true;
        Assert.True(BetterMultiplayerConfig.AllowsRelic(new Byrdpip()));
    }

    /// <summary>
    /// 「佩尔的士兵」只设了 AddsPet，没设 SpawnsPets，原版仅靠 Ancient 稀有度挡住它。
    /// 宠物开关必须把它也覆盖，否则只开稀有度就会放行并造成宠物重复。
    /// </summary>
    [Fact]
    public void PaelsLegionIsGatedByPetSwitchEvenThoughItOnlySetsAddsPet()
    {
        ResetAll();
        BetterMultiplayerConfig.UnlockRelicTrading = true;
        BetterMultiplayerConfig.AllowAncientRelics = true;

        Assert.False(BetterMultiplayerConfig.AllowsRelic(new PaelsLegion()));

        BetterMultiplayerConfig.AllowPetRelics = true;
        Assert.True(BetterMultiplayerConfig.AllowsRelic(new PaelsLegion()));
    }

    /// <summary>拾取时生效类（如古钱币）只需要拾取开关。</summary>
    [Fact]
    public void OldCoinNeedsOnlyTheOnPickupSwitch()
    {
        ResetAll();
        BetterMultiplayerConfig.UnlockRelicTrading = true;

        Assert.False(BetterMultiplayerConfig.AllowsRelic(new OldCoin()));

        BetterMultiplayerConfig.AllowUponPickupRelics = true;
        Assert.True(BetterMultiplayerConfig.AllowsRelic(new OldCoin()));
    }

    /// <summary>
    /// 已用尽类（如蜥蜴尾）只需要状态开关。
    ///
    /// 注意：IsUsedUp 是运行时状态。全新的蜥蜴尾 _wasUsed 是 false，
    /// 原版本来就允许交易；只有用过之后才被挡住。
    /// 而 new LizardTail() 拿到的是 canonical 实例，属性 setter 会抛
    /// CanonicalModelException，所以这里直接写底层字段来构造该状态。
    /// </summary>
    [Fact]
    public void LizardTailNeedsOnlyTheUsedUpSwitch()
    {
        ResetAll();
        BetterMultiplayerConfig.UnlockRelicTrading = true;

        LizardTail used = new();
        typeof(LizardTail)
            .GetField("_wasUsed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(used, true);

        Assert.True(used.IsUsedUp);
        Assert.False(used.IsTradable);
        Assert.False(BetterMultiplayerConfig.AllowsRelic(used));

        BetterMultiplayerConfig.AllowUsedUpRelics = true;
        Assert.True(BetterMultiplayerConfig.AllowsRelic(used));
    }

    /// <summary>原版本来就能交易的遗物不受影响——开关全开也照样能交易。</summary>
    [Fact]
    public void VanillaTradableRelicsAreUnaffected()
    {
        ResetAll();

        RelicModel plain = new Anchor();
        Assert.True(plain.IsTradable);

        BetterMultiplayerConfig.UnlockRelicTrading = true;
        BetterMultiplayerConfig.AllowStarterRelics = true;
        BetterMultiplayerConfig.AllowEventRelics = true;
        BetterMultiplayerConfig.AllowAncientRelics = true;
        BetterMultiplayerConfig.AllowUsedUpRelics = true;
        BetterMultiplayerConfig.AllowMeltedRelics = true;
        BetterMultiplayerConfig.AllowUponPickupRelics = true;
        BetterMultiplayerConfig.AllowPetRelics = true;

        Assert.True(TradeValidator.CanTradeRelic(plain));
    }

    /// <summary>
    /// 开关不会让原版已允许的遗物变成禁止——任何设置组合下，
    /// IsTradable 为 true 的遗物必须始终可交易。
    /// </summary>
    [Fact]
    public void SettingsNeverMakeTradableRelicsUntradable()
    {
        RelicModel[] tradable = [new Anchor(), new BloodVial(), new BagOfMarbles()];

        foreach (RelicModel relic in tradable)
        {
            ResetAll();
            Assert.True(TradeValidator.CanTradeRelic(relic));

            BetterMultiplayerConfig.UnlockRelicTrading = true;
            Assert.True(TradeValidator.CanTradeRelic(relic));
        }
    }

    /// <summary>
    /// 稀有度 + 拾取效果的组合（如金珍珠：Ancient + 拾取时生效）
    /// 需要两个开关同时打开——这是功能本身，不是 bug。
    /// </summary>
    [Fact]
    public void GoldenPearlNeedsRarityAndOnPickupSwitches()
    {
        ResetAll();
        BetterMultiplayerConfig.UnlockRelicTrading = true;

        GoldenPearl pearl = new();
        Assert.False(pearl.IsTradable);

        BetterMultiplayerConfig.AllowAncientRelics = true;
        Assert.False(BetterMultiplayerConfig.AllowsRelic(pearl));

        BetterMultiplayerConfig.AllowUponPickupRelics = true;
        Assert.True(BetterMultiplayerConfig.AllowsRelic(pearl));
    }
}
