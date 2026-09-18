using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Trading;

internal static class TradeLocalization
{
    private const string NameKey = "OPTION_BETTER_MULTIPLAYER_TRADE.name";
    private const string DescriptionKey = "OPTION_BETTER_MULTIPLAYER_TRADE.description";
    private const string AssistSmithNameKey = "OPTION_BETTER_MULTIPLAYER_ASSIST_SMITH.name";
    private const string AssistSmithDescriptionKey = "OPTION_BETTER_MULTIPLAYER_ASSIST_SMITH.description";

    internal static void Install(LocManager locManager, string language)
    {
        locManager.GetTable("rest_site_ui").MergeWith(new Dictionary<string, string>
        {
            [NameKey] = ModText.ForLanguage(language, TextKey.Trade),
            [DescriptionKey] = ModText.ForLanguage(
                language,
                TextKey.RestSiteTradeDescription),
            [AssistSmithNameKey] = ModText.ForLanguage(language, TextKey.AssistSmithName),
            [AssistSmithDescriptionKey] = ModText.ForLanguage(language, TextKey.AssistSmithDescription)
        });
    }
}

[HarmonyPatch(typeof(LocManager), nameof(LocManager.SetLanguage))]
internal static class TradeLocalizationLanguagePatch
{
    [HarmonyPostfix]
    private static void Postfix(LocManager __instance, string language)
    {
        ModText.SetLanguage(language);
        TradeLocalization.Install(__instance, language);

        // 设置页的文案。独立 try/catch：旧版 BaseLib 没有 Config API 时
        // 这里会抛 TypeLoadException，不能让它影响交易本身的本地化。
        try
        {
            BetterMultiplayer.Config.ConfigLocalization.Install(__instance, language);
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Warn($"Failed to install config localization: {ex.Message}");
        }
    }
}
