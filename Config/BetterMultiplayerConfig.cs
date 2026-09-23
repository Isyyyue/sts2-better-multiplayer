using System.Threading.Tasks;
using BaseLib.Config;
using BaseLib.Config.UI;
using BetterMultiplayer.Diagnostics;
using Godot;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace BetterMultiplayer.Config;

/// <summary>
/// 玩家可调的模组设置。BaseLib 会为每个属性自动生成一行 UI，
/// 并用 <see cref="ConfigSectionAttribute"/> 分组。
///
/// 两条硬约束（BaseLib.Config.ModConfig）：
///
/// 1) 属性必须是 static。BaseLib 的 CheckConfigProperties 只收集静态属性，
///    实例属性会被静默忽略（只留一条 warning），结果是空白的设置页。
/// 2) 命名空间的第一段必须是 "BetterMultiplayer"。ModPrefix 与配置文件路径
///    都从它推导（BaseLib.Extensions.TypePrefix），否则构造函数直接抛异常。
///    前缀 = "BETTERMULTIPLAYER-"，配置文件 = mod_configs/BetterMultiplayer.cfg。
/// </summary>
internal sealed class BetterMultiplayerConfig : SimpleModConfig
{
    // ------------------------------------------------------------------
    // 总开关（设置页上唯一的分项）
    //
    // 关着 = 原版行为，只有一件事例外：效果干净的遗物本来就不该被挡（见 CanTrade）。
    // 打开 = 剩下那批带副作用的也一并放行。
    //
    // ★ 设置页【不做分类】。试过按稀有度分、按功能分、按"交易它会出什么事"分，
    //   结论都是：分类一旦和归类规则对不上，标签就会骗人
    //   （比如「允许交易先古遗物」实际只放行三分之二的先古遗物）。
    //   与其让玩家研究分类，不如一个开关解决。
    //
    // 刻意【不加】ConfigSection：BaseLib 的 SectionTracker 在分组名为 null 时
    // 不新建分组，直接把这一行挂到根容器。加了反而会被一个只有一行的折叠标题
    // 包住。总开关应该独立于下面的分组，始终可见。
    // ------------------------------------------------------------------

    [ConfigHoverTip]
    public static bool UnlockRelicTrading { get; set; }

    // ------------------------------------------------------------------
    // 惊喜模式
    //
    // ★ 刻意【什么都不解释】：没有 ConfigHoverTip、没有 ConfigSection，
    //   文案里也不写说明。开了之后会发生什么，玩家得自己发现。
    //   改动这一项时请保持"零解释"——那是功能的一部分，不是漏写。
    //
    // 声明位置也必须留在这里：BaseLib 的 SectionTracker 碰到"没有分组的属性"
    // 时只是不新建分组，行会被挂到【当前容器】。所以放到第一个分组之后的话，
    // 这一行会被塞进那个分组里，而不是留在根容器。
    // ------------------------------------------------------------------

    public static bool SurpriseSharedGold { get; set; }

    // ------------------------------------------------------------------
    // 交易后允许重复遗物
    //
    // 关着 = 原版行为：结算时若有一方会因此持有两个同名遗物，整笔交易被拒。
    // 打开 = 放行这类交易。
    //
    // 和 UnlockRelicTrading 是两件事，互不覆盖：
    //   那一项管「这个遗物本身能不能交易」（稀有度 / 副作用），
    //   这一项管「交易完会不会撞上已有遗物」。
    //
    // 声明位置同样必须在第一个 ConfigSection 之前——理由见上面惊喜模式那段。
    // ------------------------------------------------------------------

    [ConfigHoverTip]
    public static bool AllowDuplicateRelicsAfterTrade { get; set; }

    // ------------------------------------------------------------------
    // 反馈
    //
    // 悬停提示保留了大厅按钮上那段隐私说明（上传了什么、不含什么）。
    // 大厅那个入口已经去掉，这里成了唯一的说明位置，不能省。
    // ------------------------------------------------------------------

    [ConfigSection("Feedback")]
    [ConfigButton("SendFeedbackButton")]
    [ConfigHoverTip]
    public static void SendFeedback(NConfigButton button)
    {
        // 这一行是给"点了没反应"排查用的：先确认点击有没有进来。
        // 配合 DiagnosticFeedbackService 那两条结果日志，
        // 能把"没点到"和"发了但没提示"区分开。
        BetterMultiplayerMod.Logger.Info("Diagnostic feedback button pressed.");

        if (button is null || !button.IsEnabled)
            return;

        TaskHelper.RunSafely(SendFeedbackAsync(button));
    }

    private static async Task SendFeedbackAsync(NConfigButton button)
    {
        // NClickableControl 没有 Disabled 属性，只有 IsEnabled / Disable() / Enable()。
        button.Disable();

        FeedbackSendResult result;
        try
        {
            result = await DiagnosticFeedbackService.SendAsync(button);
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Warn(
                $"Diagnostic feedback failed unexpectedly: {ex.GetType().Name}: {ex.Message}");
            result = new FeedbackSendResult(FeedbackSendStatus.NetworkFailed, string.Empty);
        }
        finally
        {
            // 服务端自带冷却，这里只管把按钮放开，让玩家还能再点。
            if (GodotObject.IsInstanceValid(button))
                button.Enable();
        }

        // ★ 必须给玩家一个看得见的结果。
        //   SendAsync 只把结果写进日志，不弹东西的话，
        //   点击在界面上就是"毫无反应"——这正是之前那个 bug。
        await FeedbackResultPopup.ShowAsync(result);
    }

    // ------------------------------------------------------------------
    // 判定
    // ------------------------------------------------------------------

    /// <summary>
    /// 这个遗物现在能不能交易。三条，命中任一就放行：
    ///
    ///   1. 原版允许（<see cref="RelicModel.IsTradable"/>）——本模组从不收紧，只放开。
    ///   2. 效果干净——被挡纯粹是因为稀有度是 起始 / 事件 / 先古。
    ///      原版 IsTradable 的最后一条只看稀有度、和效果无关，所以这类遗物
    ///      交易起来没有任何副作用，直接当正常遗物处理，不占设置项。
    ///      **这是本模组唯一一处【默认】偏离原版的地方。**
    ///   3. 总开关打开——剩下那批带副作用的（会重复触发获得时效果 / 带状态 /
    ///      附带宠物，共 62 个）一并放行。
    /// </summary>
    internal static bool CanTrade(RelicModel relic) =>
        relic.IsTradable || !HasSideEffect(relic) || UnlockRelicTrading;

    /// <summary>
    /// 交易这个遗物会不会出问题。
    ///
    /// 这五条正好对应原版 IsTradable 里"看效果"的那四条，交易时都有代价：
    ///   - 附带宠物 / 拾取时生效：RelicCmd.Obtain 会重放 AfterObtained()
    ///   - 已用尽 / 已融化：遗物带着一个状态，会一起传过去
    ///
    /// 注意 AddsPet 必须算进来：原版只查 SpawnsPets，而「佩尔的士兵」只设了
    /// AddsPet，它靠 Ancient 稀有度才没漏过去。不算进来的话它会被判成
    /// "效果干净"而默认放行，AfterObtained 再次 AddPet 造成宠物重复。
    /// </summary>
    private static bool HasSideEffect(RelicModel relic) =>
        relic.SpawnsPets ||
        relic.AddsPet ||
        relic.HasUponPickupEffect ||
        relic.IsUsedUp ||
        relic.IsMelted;
}
