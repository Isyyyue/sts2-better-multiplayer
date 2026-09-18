using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BaseLib.Config;
using BaseLib.Extensions;
using BetterMultiplayer.Config;
using MegaCrit.Sts2.Core.Helpers;
using Xunit;

namespace BetterMultiplayer.Tests;

/// <summary>
/// 设置页的键名全部靠约定拼出来，写错不会报错——BaseLib 会静默回落到
/// 原始属性名，界面上直接显示 "AllowUsedUpRelics" 这种英文标识符。
/// 这组测试用 BaseLib 真实的 GetPrefix() 和游戏的 StringHelper.Slugify()
/// 逐项核对，把"约定"变成"断言"。
/// </summary>
public sealed class ConfigLocalizationTests
{
    private static readonly Type ConfigType = typeof(BetterMultiplayerConfig);

    private static Dictionary<string, string> Entries() => ConfigLocalization.BuildEntries("zhs");

    private static IEnumerable<PropertyInfo> ConfigProperties() =>
        ConfigType.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.CanRead && p.CanWrite && p.GetMethod?.IsStatic == true);

    private static IEnumerable<MethodInfo> ButtonMethods() =>
        ConfigType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<ConfigButtonAttribute>() != null);

    /// <summary>
    /// 前缀必须和 BaseLib 从命名空间推导出来的一致。
    /// 不一致的话所有键都会落空，设置页会显示一堆英文属性名。
    /// </summary>
    [Fact]
    public void PrefixMatchesBaseLibComputation()
    {
        string actual = ConfigType.GetPrefix();
        Assert.Equal("BETTERMULTIPLAYER-", actual);
        Assert.Equal(ConfigLocalization.Prefix, actual);
    }

    /// <summary>命名空间第一段决定配置文件路径，写错会让 ModConfig 构造直接抛异常。</summary>
    [Fact]
    public void RootNamespaceMatchesModId()
    {
        Assert.Equal("BetterMultiplayer", ConfigType.GetRootNamespace());
    }

    /// <summary>BaseLib 只收集静态属性；实例属性会被静默忽略，设置页会变成空白。</summary>
    [Fact]
    public void ConfigPropertiesAreStatic()
    {
        // 只看本类声明的属性。基类 ModConfig 的 ModPrefix / ModId 是实例属性，
        // BaseLib 本来就会忽略它们，不算问题。
        PropertyInfo[] instance = ConfigType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetMethod?.IsStatic != true)
            .ToArray();

        Assert.Empty(instance);
        Assert.NotEmpty(ConfigProperties());
    }

    /// <summary>每个开关都要有行标题，键名按 BaseLib 的规则算。</summary>
    [Fact]
    public void EveryConfigPropertyHasTitle()
    {
        Dictionary<string, string> entries = Entries();
        List<string> missing = [];

        foreach (PropertyInfo property in ConfigProperties())
        {
            string key = ConfigLocalization.Prefix + StringHelper.Slugify(property.Name) + ".title";
            if (!entries.ContainsKey(key))
                missing.Add($"{property.Name} -> {key}");
        }

        Assert.Empty(missing);
    }

    /// <summary>分组标题同样按 Slugify 规则。</summary>
    [Fact]
    public void EverySectionHasTitle()
    {
        Dictionary<string, string> entries = Entries();
        List<string> missing = [];

        IEnumerable<string> sections = ConfigProperties()
            .Concat<MemberInfo>(ButtonMethods())
            .Select(m => m.GetCustomAttribute<ConfigSectionAttribute>()?.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .Distinct();

        foreach (string section in sections)
        {
            string key = ConfigLocalization.Prefix + StringHelper.Slugify(section) + ".title";
            if (!entries.ContainsKey(key))
                missing.Add($"{section} -> {key}");
        }

        Assert.Empty(missing);
    }

    /// <summary>按钮文案的键名同样走 GetLabelText -> Slugify。</summary>
    [Fact]
    public void EveryButtonHasLabel()
    {
        Dictionary<string, string> entries = Entries();
        List<string> missing = [];

        foreach (MethodInfo method in ButtonMethods())
        {
            ConfigButtonAttribute attribute = method.GetCustomAttribute<ConfigButtonAttribute>()!;
            string key = ConfigLocalization.Prefix + StringHelper.Slugify(attribute.ButtonLabelKey) + ".title";
            if (!entries.ContainsKey(key))
                missing.Add($"{method.Name} -> {key}");
        }

        Assert.Empty(missing);
    }

    /// <summary>带悬停提示的项必须同时有 .hover.title 和 .hover.desc，缺一个整条提示都不显示。</summary>
    [Fact]
    public void HoverTippedPropertiesHaveBothKeys()
    {
        Dictionary<string, string> entries = Entries();
        List<string> missing = [];
        int tipped = 0;

        foreach (PropertyInfo property in ConfigProperties())
        {
            if (property.GetCustomAttribute<ConfigHoverTipAttribute>() is not { Enabled: true })
                continue;

            tipped++;
            string slug = ConfigLocalization.Prefix + StringHelper.Slugify(property.Name);
            if (!entries.ContainsKey(slug + ".hover.title"))
                missing.Add(slug + ".hover.title");
            if (!entries.ContainsKey(slug + ".hover.desc"))
                missing.Add(slug + ".hover.desc");
        }

        Assert.True(tipped > 0, "至少应该有一项带悬停提示");
        Assert.Empty(missing);
    }

    /// <summary>ConfigVisibleIf 指向的属性必须存在且是静态的，否则依赖项永远不显示。</summary>
    [Fact]
    public void VisibilityTargetsExistAndAreStatic()
    {
        List<string> problems = [];

        foreach (PropertyInfo property in ConfigProperties())
        {
            ConfigVisibleIfAttribute? visibleIf =
                property.GetCustomAttribute<ConfigVisibleIfAttribute>();
            if (visibleIf is null)
                continue;

            PropertyInfo? target = ConfigType.GetProperty(
                visibleIf.TargetName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);

            if (target is null)
                problems.Add($"{property.Name} 指向不存在的属性 {visibleIf.TargetName}");
            else if (target.GetMethod?.IsStatic != true)
                problems.Add($"{property.Name} 指向的属性 {visibleIf.TargetName} 不是静态的");
        }

        Assert.Empty(problems);
    }

    /// <summary>反向检查：不能有拼错后没人用的孤儿键。</summary>
    [Fact]
    public void NoOrphanEntries()
    {
        Dictionary<string, string> entries = Entries();
        HashSet<string> expected = [];

        foreach (PropertyInfo property in ConfigProperties())
        {
            string slug = ConfigLocalization.Prefix + StringHelper.Slugify(property.Name);
            expected.Add(slug + ".title");
            if (property.GetCustomAttribute<ConfigHoverTipAttribute>() is { Enabled: true })
            {
                expected.Add(slug + ".hover.title");
                expected.Add(slug + ".hover.desc");
            }
        }

        foreach (MemberInfo member in ConfigProperties().Concat<MemberInfo>(ButtonMethods()))
        {
            string? section = member.GetCustomAttribute<ConfigSectionAttribute>()?.Name;
            if (!string.IsNullOrEmpty(section))
                expected.Add(ConfigLocalization.Prefix + StringHelper.Slugify(section!) + ".title");
        }

        foreach (MethodInfo method in ButtonMethods())
        {
            ConfigButtonAttribute attribute = method.GetCustomAttribute<ConfigButtonAttribute>()!;
            expected.Add(ConfigLocalization.Prefix + StringHelper.Slugify(attribute.ButtonLabelKey) + ".title");
        }

        List<string> orphans = entries.Keys.Where(k => !expected.Contains(k)).ToList();
        Assert.Empty(orphans);
    }

    /// <summary>中英两份文案都不能为空。</summary>
    [Theory]
    [InlineData("zhs")]
    [InlineData("eng")]
    public void EntriesAreNonEmpty(string language)
    {
        foreach ((string key, string value) in ConfigLocalization.BuildEntries(language))
        {
            Assert.False(string.IsNullOrWhiteSpace(value), $"{language} 的 {key} 是空的");
        }
    }
}
