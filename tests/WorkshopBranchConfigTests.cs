using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BetterMultiplayer.Tests;

/// <summary>
/// Workshop 的分支支持范围。
///
/// 背景：游戏按 Steam 分支判断能不能加载这个 Mod。分支从低到高是
///   public（正式版）→ public-beta（先行测试版）
/// 而 workshop.json 里的 minBranch / maxBranch 曾经**写反**过，
/// 结果 public-beta 玩家一进游戏就被拒绝，弹「只支持 public 版本」。
///
/// 这类错误编译器和普通测试都抓不到，只能直接读配置来守。
/// </summary>
public sealed class WorkshopBranchConfigTests
{
    /// <summary>Steam 分支，从低到高。</summary>
    private static readonly string[] BranchOrder = ["public", "public-beta"];

    [Fact]
    public void MinBranchIsNotHigherThanMaxBranch()
    {
        JsonElement config = ReadWorkshopConfig();

        string? min = config.GetProperty("minBranch").GetString();
        string? max = config.GetProperty("maxBranch").GetString();

        int minIndex = Array.IndexOf(BranchOrder, min);
        int maxIndex = Array.IndexOf(BranchOrder, max);

        Assert.True(minIndex >= 0, $"未知的 minBranch：{min}");
        Assert.True(maxIndex >= 0, $"未知的 maxBranch：{max}");

        // ★ 写反时 minIndex > maxIndex，范围是空的 ——
        //   public-beta 玩家会被判定"超出支持范围"而进不去游戏。
        Assert.True(
            minIndex <= maxIndex,
            $"minBranch({min}) 高于 maxBranch({max})，支持范围为空。");
    }

    /// <summary>支持范围必须真的覆盖两个分支，不能只覆盖一个。</summary>
    [Fact]
    public void BranchRangeCoversBothPublicAndBeta()
    {
        JsonElement config = ReadWorkshopConfig();

        Assert.Equal("public", config.GetProperty("minBranch").GetString());
        Assert.Equal("public-beta", config.GetProperty("maxBranch").GetString());
    }

    /// <summary>
    /// 两个脚本写 branch-support 文本时必须是"低 → 高"。
    ///
    /// ★ 曾经写成 `maxBranch through minBranch`，靠配置也写反才互相抵消——
    ///   输出的文本看着是对的，实际范围是空的。谁把顺序换回来，这里会红。
    /// </summary>
    [Theory]
    [InlineData("tools/prepare-release.ps1")]
    [InlineData("tools/publish-workshop.ps1")]
    public void BranchSupportTextIsWrittenLowToHigh(string relativePath)
    {
        string script = File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath));

        Assert.Contains(
            "$($workshopConfig.minBranch) through $($workshopConfig.maxBranch)",
            script);

        Assert.DoesNotContain(
            "$($workshopConfig.maxBranch) through $($workshopConfig.minBranch)",
            script);
    }

    private static JsonElement ReadWorkshopConfig()
    {
        string path = Path.Combine(FindRepoRoot(), "workshop", "workshop.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        // JsonDocument 会随 using 释放，这里返回克隆，调用方才能继续读。
        return document.RootElement.Clone();
    }

    /// <summary>从测试输出目录往上找仓库根（含 workshop/workshop.json 的那层）。</summary>
    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "workshop", "workshop.json")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "找不到仓库根（往上找不到 workshop/workshop.json）。");
    }
}
