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
    // 总开关
    //
    // 刻意【不加】ConfigSection：BaseLib 的 SectionTracker 在分组名为 null 时
    // 不新建分组，直接把这一行挂到根容器。加了反而会被一个只有一行的折叠标题
    // 包住。总开关应该独立于下面的分组，始终可见。
    // ------------------------------------------------------------------

    public static bool UnlockRelicTrading { get; set; }

    // ------------------------------------------------------------------
    // 无副作用：稀有度类
    // ------------------------------------------------------------------

    [ConfigSection("RelicRarity")]
    [ConfigVisibleIf(nameof(UnlockRelicTrading), true)]
    public static bool AllowStarterRelics { get; set; }

    [ConfigSection("RelicRarity")]
    [ConfigVisibleIf(nameof(UnlockRelicTrading), true)]
    public static bool AllowEventRelics { get; set; }

    [ConfigSection("RelicRarity")]
    [ConfigVisibleIf(nameof(UnlockRelicTrading), true)]
    public static bool AllowAncientRelics { get; set; }

    // ------------------------------------------------------------------
    // 无副作用：状态类
    //
    // 遗物状态通过引擎的 [SavedProperty] 机制随交易无损传递
    // （RelicModel.ToSerializable -> SavedProperties.From -> Props.Fill），
    // 接收方拿到的就是前主人那个状态，不会被"洗白"。
    // 例：ToyBox 的 CombatsSeen、LizardTail 的 WasUsed 都带 [SavedProperty]，
    //     基类的 IsMelted 本身也是 [SavedProperty]。
    // ------------------------------------------------------------------

    [ConfigSection("RelicState")]
    [ConfigVisibleIf(nameof(UnlockRelicTrading), true)]
    public static bool AllowUsedUpRelics { get; set; }

    [ConfigSection("RelicState")]
    [ConfigVisibleIf(nameof(UnlockRelicTrading), true)]
    public static bool AllowMeltedRelics { get; set; }

    // ------------------------------------------------------------------
    // 会重复触发效果
    //
    // RelicCmd.Obtain 内部会执行 await relic.AfterObtained()，
    // 也就是"获得时"逻辑会被重放一次。默认关闭。
    // ------------------------------------------------------------------

    [ConfigSection("RelicSideEffect")]
    [ConfigVisibleIf(nameof(UnlockRelicTrading), true)]
    [ConfigHoverTip]
    public static bool AllowUponPickupRelics { get; set; }

    [ConfigSection("RelicSideEffect")]
    [ConfigVisibleIf(nameof(UnlockRelicTrading), true)]
    [ConfigHoverTip]
    public static bool AllowPetRelics { get; set; }

    // ------------------------------------------------------------------
    // 反馈
    // ------------------------------------------------------------------

    [ConfigSection("Feedback")]
    [ConfigButton("SendFeedbackButton")]
    public static void SendFeedback(NConfigButton button)
    {
        if (button is null || !button.IsEnabled)
            return;

        TaskHelper.RunSafely(SendFeedbackAsync(button));
    }

    private static async Task SendFeedbackAsync(NConfigButton button)
    {
        // NClickableControl 没有 Disabled 属性，只有 IsEnabled / Disable() / Enable()。
        button.Disable();
        try
        {
            await DiagnosticFeedbackService.SendAsync(button);
        }
        finally
        {
            // 服务端自带冷却，这里只管把按钮放开，让玩家还能再点。
            if (GodotObject.IsInstanceValid(button))
                button.Enable();
        }
    }

    // ------------------------------------------------------------------
    // 判定
    // ------------------------------------------------------------------

    /// <summary>
    /// 判断一个被原版标记为不可交易的遗物，是否因为玩家设置而允许交易。
    ///
    /// 逐条对照 RelicModel.IsTradable 的否决条件——原版是"任一条件命中即禁止"，
    /// 这里改成"每一条都必须被对应的开关单独放开"。
    /// 所以像「幼年异鸟」（事件稀有度 + 召唤宠物 + 拾取时生效）需要三个开关同时打开。
    /// </summary>
    internal static bool AllowsRelic(RelicModel relic)
    {
        if (!UnlockRelicTrading)
            return false;

        if (relic.IsUsedUp && !AllowUsedUpRelics)
            return false;
        if (relic.HasUponPickupEffect && !AllowUponPickupRelics)
            return false;
        if (relic.IsMelted && !AllowMeltedRelics)
            return false;

        // 原版 IsTradable 只检查 SpawnsPets。但「佩尔的士兵」(PaelsLegion)
        // 只设了 AddsPet，没设 SpawnsPets —— 它仅靠 Ancient 稀有度被挡住。
        // 本开关的语义是"召唤宠物类"，所以两个标志都要算，
        // 否则玩家只打开稀有度开关就会让它溜过去，而它的 AfterObtained
        // 会再次 SummonPet，造成宠物重复。
        if ((relic.SpawnsPets || relic.AddsPet) && !AllowPetRelics)
            return false;

        return relic.Rarity switch
        {
            RelicRarity.Starter => AllowStarterRelics,
            RelicRarity.Event => AllowEventRelics,
            RelicRarity.Ancient => AllowAncientRelics,
            _ => true
        };
    }
}
