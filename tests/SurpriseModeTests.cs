using System;
using System.Linq;
using System.Reflection;
using BaseLib.Config;
using BetterMultiplayer.Config;
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
    public SurpriseModeTests() => Reset();

    public void Dispose() => Reset();

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
}
