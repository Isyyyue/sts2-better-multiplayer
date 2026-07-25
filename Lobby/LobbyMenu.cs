using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.addons.mega_text;
using BetterMultiplayer.Trading;
using BetterMultiplayer.UI;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Lobby;

internal sealed class LobbyMenu
{
    private enum Page
    {
        Entry,
        Join,
        Create,
        SaveChoice
    }

    private readonly NMultiplayerSubmenu _submenu;
    private readonly NBackButton _submenuBackButton;
    private readonly Control _root;
    private readonly VBoxContainer _body;
    private readonly Label _status;
    private readonly NBackButton _entryBackButton;
    private CancellationTokenSource? _requestCancellation;
    private bool _closed;
    private LobbyMenuExitReason _exitReason = LobbyMenuExitReason.UserBack;
    private Page _page;
    private string _createRoomName = string.Empty;
    private string _createPassword = string.Empty;

    private LobbyMenu(NMultiplayerSubmenu submenu)
    {
        _submenu = submenu;
        _submenuBackButton = Traverse.Create(_submenu)
            .Field("_backButton")
            .GetValue<NBackButton>();
        _submenuBackButton.Disable();
        _submenuBackButton.MoveToHidePosition();
        _root = UiFactory.CreateBlurredOverlay(out _body, out _status);
        submenu.AddChild(_root);
        _entryBackButton = UiFactory.CreateOfficialBackButton(HandleBack);
        _root.AddChild(_entryBackButton);
        ShowEntry();
    }

    internal static void Show(NMultiplayerSubmenu submenu)
    {
        submenu.GetNodeOrNull<Control>("BetterMultiplayerOverlay")?.QueueFree();
        _ = new LobbyMenu(submenu);
    }

    private void ShowEntry()
    {
        _page = Page.Entry;
        ClearBody();
        SetStatus(string.Empty, error: false);
        ShowBackButton();

        NSubmenuButton joinTemplate = Traverse.Create(_submenu)
            .Field("_joinButton")
            .GetValue<NSubmenuButton>();
        NSubmenuButton createTemplate = Traverse.Create(_submenu)
            .Field("_hostButton")
            .GetValue<NSubmenuButton>();

        HBoxContainer choices = new()
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        choices.AddThemeConstantOverride("separation", 34);
        choices.AddChild(CreateOfficialChoice(
            joinTemplate,
            "RoomJoinButton",
            ModText.Get(TextKey.Join),
            TradeAssets.JoinRoomIcon,
            ShowJoin,
            hideDescription: false,
            description: ModText.Get(TextKey.JoinRoomDescription)));
        choices.AddChild(CreateOfficialChoice(
            createTemplate,
            "RoomCreateButton",
            ModText.Get(TextKey.Create),
            TradeAssets.CreateRoomIcon,
            ShowCreate,
            hideDescription: false,
            description: ModText.Get(TextKey.CreateRoomDescription)));
        _body.AddChild(choices);
    }

    private void ShowJoin()
    {
        _page = Page.Join;
        ClearBody();
        SetStatus(string.Empty, error: false);
        ShowBackButton();

        LineEdit roomName = CreateLargeInput(ModText.Get(TextKey.RoomNamePlaceholder), RoomText.MaxRoomNameLength);
        LineEdit password = CreateLargeInput(ModText.Get(TextKey.RoomPasswordPlaceholder), RoomText.MaxPasswordLength);
        VBoxContainer form = CreateForm();
        form.AddChild(CreateFormHeading(ModText.Get(TextKey.JoinRoom)));
        form.AddChild(CreateField(ModText.Get(TextKey.RoomName), roomName));
        form.AddChild(CreateField(ModText.Get(TextKey.RoomPassword), password));

        Button join = null!;
        join = UiFactory.Button(ModText.Get(TextKey.JoinRoom), () =>
        {
            if (!ValidateCredentials(roomName.Text, password.Text))
                return;
            TaskHelper.RunSafely(JoinByCredentials(roomName.Text, password.Text, join));
        }, primary: true);
        ConfigureMainAction(join);
        form.AddChild(CreateActionRow(join));
        _body.AddChild(Center(CreateTexturedForm(form)));
        roomName.GrabFocus();
    }

    private void ShowCreate()
    {
        _page = Page.Create;
        ClearBody();
        SetStatus(string.Empty, error: false);
        ShowBackButton();

        LineEdit roomName = CreateLargeInput(ModText.Get(TextKey.RoomNamePlaceholder), RoomText.MaxRoomNameLength);
        LineEdit password = CreateLargeInput(ModText.Get(TextKey.RequiredPasswordPlaceholder), RoomText.MaxPasswordLength);
        roomName.Text = _createRoomName;
        password.Text = _createPassword;
        VBoxContainer form = CreateForm();
        form.AddChild(CreateFormHeading(ModText.Get(TextKey.CreateRoom)));
        form.AddChild(CreateField(ModText.Get(TextKey.RoomName), roomName));
        form.AddChild(CreateField(ModText.Get(TextKey.RoomPassword), password));

        Button create = null!;
        create = UiFactory.Button(ModText.Get(TextKey.CreateRoom), () =>
        {
            if (!ValidateCredentials(roomName.Text, password.Text))
                return;
            _createRoomName = roomName.Text;
            _createPassword = password.Text;
            TaskHelper.RunSafely(CreateRoom(roomName.Text, password.Text, create));
        }, primary: true);
        ConfigureMainAction(create);
        form.AddChild(CreateActionRow(create));
        _body.AddChild(Center(CreateTexturedForm(form)));
        roomName.GrabFocus();
    }

    private async Task JoinByCredentials(string roomName, string password, Button button)
    {
        if (!SteamInitializer.Initialized)
        {
            SetStatus(ModText.Get(TextKey.SteamUnavailableJoin), error: true);
            return;
        }

        button.Disabled = true;
        SetStatus(ModText.Get(TextKey.SearchingForRoom), error: false);
        _requestCancellation = new CancellationTokenSource();
        try
        {
            RoomRecord? room = await LobbyDirectory.FindMatching(
                roomName,
                password,
                _requestCancellation.Token);
            if (room is null)
            {
                SetStatus(ModText.Get(TextKey.RoomNotFound), error: true);
                return;
            }
            if (room.PlayerCount >= room.Capacity)
            {
                SetStatus(ModText.Get(TextKey.RoomFull), error: true);
                return;
            }
            if (!JoinContext.Begin(room, password, out string error))
            {
                SetStatus(error, error: true);
                return;
            }

            await JoinRoom(room.LobbyId);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Error($"Joining by room name failed: {ex}");
            SetStatus(ModText.Get(TextKey.JoinFailed), error: true);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(button))
                button.Disabled = false;
        }
    }

    private async Task CreateRoom(string roomName, string password, Button button)
    {
        if (!SteamInitializer.Initialized)
        {
            SetStatus(ModText.Get(TextKey.SteamUnavailableCreate), error: true);
            return;
        }

        button.Disabled = true;
        SetStatus(ModText.Get(TextKey.CheckingRoom), error: false);
        _requestCancellation = new CancellationTokenSource();
        try
        {
            if (await LobbyDirectory.HasCredentialCollision(
                    roomName,
                    password,
                    _requestCancellation.Token))
            {
                SetStatus(ModText.Get(TextKey.DuplicateRoom), error: true);
                return;
            }
            if (!RoomSession.BeginHosting(roomName, password, out string error))
            {
                SetStatus(error, error: true);
                return;
            }

            if (SaveManager.Instance.HasMultiplayerRunSave)
                ShowSaveChoice();
            else
                StartNewRun();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Error($"Room validation before hosting failed: {ex}");
            SetStatus(ModText.Get(TextKey.CreateFailed), error: true);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(button))
                button.Disabled = false;
        }
    }

    private void ShowSaveChoice()
    {
        _page = Page.SaveChoice;
        ClearBody();
        SetStatus(string.Empty, error: false);
        ShowBackButton();

        NSubmenuButton loadTemplate = Traverse.Create(_submenu)
            .Field("_loadButton")
            .GetValue<NSubmenuButton>();
        NSubmenuButton abandonTemplate = Traverse.Create(_submenu)
            .Field("_abandonButton")
            .GetValue<NSubmenuButton>();

        HBoxContainer choices = new()
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        choices.AddThemeConstantOverride("separation", 34);
        choices.AddChild(CreateOfficialChoice(
            loadTemplate,
            "RoomLoadButton",
            title: null,
            icon: null,
            StartSavedRun,
            hideDescription: false));
        choices.AddChild(CreateOfficialChoice(
            abandonTemplate,
            "RoomAbandonButton",
            title: null,
            icon: null,
            () => TaskHelper.RunSafely(ConfirmAbandonAndStart()),
            hideDescription: false));
        _body.AddChild(choices);
    }

    private void StartSavedRun()
    {
        PlatformType platformType = SteamInitializer.Initialized && !CommandLineHelper.HasArg("fastmp")
            ? PlatformType.Steam
            : PlatformType.None;
        ReadSaveResult<SerializableRun> result = SaveManager.Instance.LoadAndCanonicalizeMultiplayerRunSave(
            PlatformUtil.GetLocalPlayerId(platformType));
        if (!result.Success || result.SaveData is null)
        {
            SetStatus(ModText.Get(TextKey.MultiplayerSaveUnavailable), error: true);
            return;
        }

        StartHost(() => _submenu.StartHost(result.SaveData));
    }

    private async Task ConfirmAbandonAndStart()
    {
        NGenericPopup? popup = NGenericPopup.Create();
        NModalContainer? modal = NModalContainer.Instance;
        if (popup is null || modal is null)
            return;
        modal.Add(popup);
        bool confirmed = await popup.WaitForConfirmation(
            new LocString("main_menu_ui", "ABANDON_RUN_CONFIRMATION.body"),
            new LocString("main_menu_ui", "ABANDON_RUN_CONFIRMATION.header"),
            new LocString("main_menu_ui", "GENERIC_POPUP.cancel"),
            new LocString("main_menu_ui", "GENERIC_POPUP.confirm"));
        if (!confirmed)
            return;

        SaveManager.Instance.DeleteCurrentMultiplayerRun();
        StartNewRun();
    }

    private void StartNewRun() => StartHost(() => _submenu.OnHostPressed(null!));

    private void StartHost(Action start)
    {
        _exitReason = LobbyMenuExitReason.HostHandoff;
        Close();
        start();
    }

    private async Task JoinRoom(ulong lobbyId)
    {
        try
        {
            IClientConnectionInitializer initializer = SteamClientConnectionInitializer.FromLobby(lobbyId);
            // MainMenu.JoinGame would push the already-open multiplayer submenu a second time.
            await LobbyJoinHandoff.Run(
                _submenu,
                static submenu => submenu.OnJoinFriendsPressed(),
                () =>
                {
                    _exitReason = LobbyMenuExitReason.JoinHandoff;
                    Close();
                },
                joinScreen => joinScreen.JoinGameAsync(initializer));
        }
        finally
        {
            JoinContext.Clear();
        }
    }

    private bool ValidateCredentials(string roomName, string password)
    {
        if (RoomText.NormalizeRoomName(roomName).Length == 0 || password.Length == 0)
        {
            SetStatus(ModText.Get(TextKey.EnterRoomCredentials), error: true);
            return false;
        }
        if (!RoomText.IsValidPassword(password))
        {
            SetStatus(ModText.Get(TextKey.InvalidPassword), error: true);
            return false;
        }
        return true;
    }

    private NSubmenuButton CreateOfficialChoice(
        NSubmenuButton template,
        string name,
        string? title,
        Texture2D? icon,
        Action onReleased,
        bool hideDescription,
        string? description = null)
    {
        NSubmenuButton choice = (NSubmenuButton)template.Duplicate(14);
        choice.Name = name;
        choice.Visible = true;

        if (title is not null)
        {
            MegaLabel titleLabel = choice.GetNode<MegaLabel>("%Title");
            titleLabel.SetTextAutoSize(title);
        }
        if (icon is not null)
        {
            TextureRect image = choice.GetNode<TextureRect>("Icon");
            image.Texture = icon;
        }
        if (hideDescription)
        {
            MegaRichTextLabel descriptionLabel = choice.GetNode<MegaRichTextLabel>("%Description");
            descriptionLabel.Text = string.Empty;
            descriptionLabel.Visible = false;
        }
        else if (description is not null)
        {
            MegaRichTextLabel descriptionLabel = choice.GetNode<MegaRichTextLabel>("%Description");
            descriptionLabel.Text = description;
            descriptionLabel.Visible = true;
        }

        choice.Connect(
            NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => onReleased()));
        return choice;
    }

    private void HandleBack()
    {
        switch (_page)
        {
            case Page.Entry:
                Close();
                break;
            case Page.Join:
            case Page.Create:
                ShowEntry();
                break;
            case Page.SaveChoice:
                RoomSession.CancelPending();
                ShowCreate();
                break;
        }
    }

    private static Label CreateFormHeading(string heading)
    {
        Label title = UiFactory.Label(heading, 32, UiFactory.Accent);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.CustomMinimumSize = new Vector2(0, 52);
        return title;
    }

    private static VBoxContainer CreateForm()
    {
        VBoxContainer form = new()
        {
            CustomMinimumSize = new Vector2(600, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        form.AddThemeConstantOverride("separation", 34);
        return form;
    }

    private static Control CreateTexturedForm(VBoxContainer form)
    {
        Control shell = new()
        {
            CustomMinimumSize = new Vector2(780, 780)
        };

        Control background = UiFactory.CreateOfficialPaperBackground();
        background.Name = "PopupPaperBackground";
        background.MouseFilter = Control.MouseFilterEnum.Ignore;
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        shell.AddChild(background);

        MarginContainer margin = new();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 90);
        margin.AddThemeConstantOverride("margin_right", 90);
        margin.AddThemeConstantOverride("margin_top", 86);
        margin.AddThemeConstantOverride("margin_bottom", 86);
        margin.AddChild(form);
        shell.AddChild(margin);
        return shell;
    }

    private static LineEdit CreateLargeInput(string placeholder, int maxLength)
    {
        LineEdit input = UiFactory.LineEdit(placeholder, maxLength: maxLength);
        input.CustomMinimumSize = new Vector2(0, 88);
        input.AddThemeFontSizeOverride("font_size", 26);
        return input;
    }

    private static VBoxContainer CreateField(string label, Control input)
    {
        VBoxContainer field = new();
        field.AddThemeConstantOverride("separation", 10);
        field.AddChild(UiFactory.Label(label, 24));
        input.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        field.AddChild(input);
        return field;
    }

    private static HBoxContainer CreateActionRow(Button action)
    {
        HBoxContainer row = new() { Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(action);
        return row;
    }

    private static CenterContainer Center(Control control)
    {
        CenterContainer center = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        center.AddChild(control);
        return center;
    }

    private static void ConfigureMainAction(Button button)
    {
        button.CustomMinimumSize = new Vector2(280, 76);
        button.AddThemeFontSizeOverride("font_size", 24);
    }

    private void ShowBackButton()
    {
        _entryBackButton.Visible = true;
        _entryBackButton.Enable();
    }

    private void ClearBody()
    {
        foreach (Node child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void SetStatus(string text, bool error)
    {
        if (!GodotObject.IsInstanceValid(_status))
            return;
        _status.Text = ModText.Resolve(text);
        _status.Visible = text.Length > 0;
        _status.AddThemeColorOverride("font_color", error ? UiFactory.Danger : UiFactory.TextMuted);
    }

    private void Close()
    {
        if (_closed)
            return;
        _closed = true;
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = null;
        LobbyMenuClosePlan closePlan = LobbyMenuClosePlan.For(_exitReason, _submenu.Visible);
        if (closePlan.CancelPendingRoom)
            RoomSession.CancelPending();
        if (GodotObject.IsInstanceValid(_entryBackButton))
            _entryBackButton.Disable();
        if (GodotObject.IsInstanceValid(_root))
            _root.QueueFree();
        if (closePlan.RestoreParentBackButton && GodotObject.IsInstanceValid(_submenuBackButton))
        {
            _submenuBackButton.MoveToHidePosition();
            _submenuBackButton.Enable();
        }
    }
}
