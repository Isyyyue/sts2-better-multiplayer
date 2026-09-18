using System.Collections.Generic;
using BetterMultiplayer.Localization;
using MegaCrit.Sts2.Core.Localization;

namespace BetterMultiplayer.Config;

/// <summary>
/// 把设置页需要的文案装进游戏的 settings_ui 表。
///
/// BaseLib 的键名规则（BaseLib.Config.ModConfig.GetLabelText 与
/// BaseLib.Config.UI.NConfigOptionRow.AddHoverTip）：
///
///     行标题      {ModPrefix}{Slugify(属性名)}.title
///     悬停标题    {ModPrefix}{Slugify(属性名)}.hover.title
///     悬停说明    {ModPrefix}{Slugify(属性名)}.hover.desc
///     分组标题    {ModPrefix}{Slugify(分组名)}.title
///
/// 其中 ModPrefix = 命名空间第一段大写 + "-"
/// （BaseLib.Extensions.TypePrefix.GetPrefix），
/// 本类命名空间是 BetterMultiplayer.Config，所以前缀是 "BETTERMULTIPLAYER-"。
/// Slugify 把 CamelCase 拆成下划线并转大写（StringHelper.Slugify）。
///
/// 键名写错不会报错——BaseLib 会静默回落到原始属性名，
/// 界面上直接显示 "AllowUsedUpRelics" 这种英文标识符。
/// 所以 <see cref="BuildEntries"/> 保持纯函数，由 ConfigLocalizationTests
/// 拿真实的 Slugify 逐个核对。
/// </summary>
internal static class ConfigLocalization
{
    internal const string Prefix = "BETTERMULTIPLAYER-";
    internal const string Table = "settings_ui";

    /// <summary>设置页用到的全部文案。language 为游戏的语言代码。</summary>
    internal static Dictionary<string, string> BuildEntries(string language) => new()
    {
        // 分组标题（总开关没有分组，见 BetterMultiplayerConfig 的说明）
        [Prefix + "RELIC_RARITY.title"] = Text(language, TextKey.SettingRelicRaritySection),
        [Prefix + "RELIC_STATE.title"] = Text(language, TextKey.SettingRelicStateSection),
        [Prefix + "RELIC_SIDE_EFFECT.title"] = Text(language, TextKey.SettingRelicSideEffectSection),
        [Prefix + "FEEDBACK.title"] = Text(language, TextKey.SettingFeedbackSection),

        // 总开关
        [Prefix + "UNLOCK_RELIC_TRADING.title"] = Text(language, TextKey.SettingUnlockRelicTrading),

        // 稀有度类
        [Prefix + "ALLOW_STARTER_RELICS.title"] = Text(language, TextKey.SettingAllowStarterRelics),
        [Prefix + "ALLOW_EVENT_RELICS.title"] = Text(language, TextKey.SettingAllowEventRelics),
        [Prefix + "ALLOW_ANCIENT_RELICS.title"] = Text(language, TextKey.SettingAllowAncientRelics),

        // 状态类
        [Prefix + "ALLOW_USED_UP_RELICS.title"] = Text(language, TextKey.SettingAllowUsedUpRelics),
        [Prefix + "ALLOW_MELTED_RELICS.title"] = Text(language, TextKey.SettingAllowMeltedRelics),

        // 会重复触发效果（带悬停说明）
        [Prefix + "ALLOW_UPON_PICKUP_RELICS.title"] = Text(language, TextKey.SettingAllowUponPickupRelics),
        [Prefix + "ALLOW_UPON_PICKUP_RELICS.hover.title"] =
            Text(language, TextKey.SettingAllowUponPickupRelics),
        [Prefix + "ALLOW_UPON_PICKUP_RELICS.hover.desc"] =
            Text(language, TextKey.SettingAllowUponPickupRelicsTip),

        [Prefix + "ALLOW_PET_RELICS.title"] = Text(language, TextKey.SettingAllowPetRelics),
        [Prefix + "ALLOW_PET_RELICS.hover.title"] = Text(language, TextKey.SettingAllowPetRelics),
        [Prefix + "ALLOW_PET_RELICS.hover.desc"] = Text(language, TextKey.SettingAllowPetRelicsTip),

        // 按钮
        [Prefix + "SEND_FEEDBACK_BUTTON.title"] = Text(language, TextKey.SettingSendFeedbackButton)
    };

    internal static void Install(LocManager locManager, string language)
    {
        locManager.GetTable(Table).MergeWith(BuildEntries(language));
    }

    private static string Text(string language, TextKey key) =>
        ModText.ForLanguage(language, key);
}
