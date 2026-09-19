using System;
using System.Reflection;
using BaseLib.Config;
using BaseLib.Config.UI;
using BetterMultiplayer.Localization;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace BetterMultiplayer.Config;

/// <summary>
/// 在设置列表里给本 Mod 插一行【独立入口】，效果和「东尼算法」那一行一样：
/// 直接打开本 Mod 的设置页，而不是先钻进 BaseLib 的模组列表再挑一次。
///
/// 做法沿用 BaseLib 自己的注入方式（BaseLib.Patches.Utils.InjectSettingsModConfigPatch）：
/// 复制游戏自带的那行「Modding」，改名、换文案、换回调。
/// 唯一区别在回调里先写 <see cref="BaseLibConfig.LastModConfigModId"/> ——
/// BaseLib 的 <c>NModConfigSubmenu.OnSubmenuShown</c> 会读这个值并直接停在对应模组上，
/// 所以不用自己重写一套设置 UI。
///
/// 没有走东尼算法那条「自写 NSubmenu + patch NMainMenuSubmenuStack.GetSubmenuType」的路：
/// 那需要自己重建整套设置控件（约 4 倍代码），而这里复用 BaseLib 已经生成好的行，
/// 成本和风险都低得多。
///
/// 失败必须不影响 Mod 本体：交易功能不依赖设置页，所以这里全部吞异常只记日志。
/// </summary>
[HarmonyPatch(typeof(NSettingsScreen), "_Ready")]
internal static class BetterMultiplayerSettingsEntry
{
    private const string RowName = "BetterMultiplayerSettings";
    private const string ButtonName = "BetterMultiplayerSettingsButton";
    private const string DividerName = "BetterMultiplayerSettingsDivider";

    private const string GeneralSettingsPath = "ScrollContainer/Mask/Clipper/GeneralSettings";
    private const string VBoxPath = "VBoxContainer/";

    /// <summary>复制模板行时用的 Godot 复制标志（信号 + 分组 + 脚本 + 实例化）。</summary>
    private const int DuplicateFlags = 15;

    /// <summary>
    /// BaseLib 的「上次打开的模组」属性。类型 BaseLib.Config.BaseLibConfig 是
    /// internal，所以只能反射拿；拿不到不影响功能，只是打开后停在模组列表上。
    /// </summary>
    private static readonly PropertyInfo? LastModConfigModId = typeof(ModConfig)
        .Assembly
        .GetType("BaseLib.Config.BaseLibConfig")
        ?.GetProperty("LastModConfigModId", BindingFlags.Public | BindingFlags.Static);

    public static void Postfix(NSettingsScreen __instance)
    {
        try
        {
            Inject(__instance);
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Warn(
                $"Could not add the standalone settings entry: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Inject(NSettingsScreen screen)
    {
        Control? general = ((Node)screen).GetNodeOrNull<Control>(GeneralSettingsPath);
        if (general is null)
            return;

        // 已经插过就不再插（设置页被重建时会重新走 _Ready）。
        if (((Node)general).GetNodeOrNull<Node>(VBoxPath + RowName) is not null)
            return;

        // 「Modding」行天生就是「一行标签 + 一个按钮」，拿它当模板最稳。
        MarginContainer? template = ((Node)general).GetNodeOrNull<MarginContainer>(VBoxPath + "Modding");
        if (template is null)
            return;

        MarginContainer row = (MarginContainer)((Node)template).Duplicate(DuplicateFlags);
        ((Node)row).UniqueNameInOwner = false;
        ((Node)row).Name = RowName;
        ((CanvasItem)row).Visible = true;

        Control? button = ((Node)row).GetNodeOrNull<Control>("ModdingButton");
        if (button is null)
            return;

        ((Node)button).Name = ButtonName;
        ((Node)button).UniqueNameInOwner = true;
        ((Node)button).Owner = screen;

        SetText(((Node)row).GetNodeOrNull<Node>("Label"), ModText.Get(TextKey.SettingsEntryLabel));
        SetText(((Node)button).GetNodeOrNull<Node>("Label"), ModText.Get(TextKey.SettingsEntryButton));

        ((GodotObject)button).Connect(
            NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => Open(screen)),
            0u);

        // 和 BaseLib 自己那行一样，上面补一条分隔线；拿不到分隔线就只插行。
        Control? dividerSource = ((Node)general).GetNodeOrNull<Control>(VBoxPath + "SendFeedbackDivider");
        if (dividerSource is null)
        {
            ((Node)template).AddSibling((Node)(object)row, false);
            return;
        }

        Node divider = ((Node)dividerSource).Duplicate(DuplicateFlags);
        divider.Name = DividerName;
        ((Node)template).AddSibling(divider, false);
        divider.AddSibling((Node)(object)row, false);
    }

    private static void Open(NSettingsScreen screen)
    {
        // 只有在主菜单的设置页才能推子菜单；游戏内设置页的栈类型不同，按不动就算了。
        if (((NSubmenu)screen)._stack is not NMainMenuSubmenuStack stack)
            return;

        RememberTargetMod();
        stack.PushSubmenuType<NModConfigSubmenu>();
    }

    /// <summary>
    /// BaseLib 的模组配置页会读「上次打开的模组」来决定停在哪个模组上
    /// （<c>NModConfigSubmenu.OnSubmenuShown</c>），所以先写上本 Mod 的 id，
    /// 页面就会直接落到本 Mod，跳过模组列表那一步。
    /// </summary>
    private static void RememberTargetMod()
    {
        if (LastModConfigModId is null)
        {
            // BaseLib 换了内部结构。功能不受影响，只是会停在模组列表上。
            BetterMultiplayerMod.Logger.Warn(
                "BaseLib.Config.BaseLibConfig.LastModConfigModId not found; " +
                "the settings entry will open the mod list instead of this mod's page.");
            return;
        }

        try
        {
            LastModConfigModId.SetValue(null, BetterMultiplayerMod.ModId);
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Warn(
                $"Could not remember the target mod for the settings page: {ex.GetType().Name}");
        }
    }

    /// <summary>行的标签是 RichTextLabel，按钮的标签是 Label，两种都兜住。</summary>
    private static void SetText(Node? node, string text)
    {
        switch (node)
        {
            case RichTextLabel rich:
                rich.Text = text;
                break;
            case Label label:
                label.Text = text;
                break;
        }
    }
}
