using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using BetterMultiplayer.Trading.Messages;
using BetterMultiplayer.UI;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Trading;

internal sealed class TradeOverlay
{
    private enum OfferItemType
    {
        Card,
        Relic,
        Potion
    }

    private static readonly Color SlotSurface = new("101417");
    private static readonly Color SelectedSurface = new("29352f");

    private readonly Control _root;
    private readonly VBoxContainer _body;
    private readonly Label _status;
    private readonly NBackButton _backButton;
    private readonly Node _sourceParent;
    private readonly TradeLocation _location;
    private TradeOffer _draft = new();
    private ulong _draftSessionId;
    private bool _offerUpdatePending;
    private bool _announced;
    private bool _closed;
    private CancellationTokenSource? _offerSendCancellation;
    private Button? _confirmButton;
    private LineEdit? _goldInput;
    private bool _confirmAfterOfferSync;
    private OfferItemType? _selectionType;
    private List<int> _selectionIndices = [];

    private TradeOverlay(Node parent, TradeLocation location)
    {
        _sourceParent = parent;
        _location = location;
        _root = UiFactory.CreateTexturedOverlay(
            ModText.Get(location == TradeLocation.RestSite ? TextKey.RestSiteTrade : TextKey.GoldTrade),
            HandleBack,
            out _body,
            out _status,
            out _backButton);
        _root.Name = "BetterMultiplayerTradeOverlay";
        _root.TreeExiting += OnTreeExiting;
        _sourceParent.TreeExiting += OnSourceTreeExiting;
        (NModalContainer.Instance ?? parent).AddChild(_root);

        TradeStateStore.Changed += OnStateChanged;
        SyncDraft();
        Render();

        if ((_location == TradeLocation.Merchant || !TradeUsageTracker.HasUsed(TradeNetwork.LocalPlayerId)) &&
            CanUseNetwork())
        {
            try
            {
                _announced = true;
                TradeNetwork.SendRequest(new AvailabilityRequest
                {
                    Available = true,
                    Location = _location,
                    ReportedGold = LocalGold()
                });
            }
            catch (Exception ex)
            {
                _announced = false;
                BetterMultiplayerMod.Logger.Error($"Publishing trade availability failed: {ex}");
                SetStatus(ModText.Get(TextKey.TradeNetworkUnavailable), error: true);
            }
        }
    }

    internal static void Show(Node parent, TradeLocation location)
    {
        BetterMultiplayerMod.Logger.Info($"Trade overlay shown: location={location}");
        Node uiParent = NModalContainer.Instance ?? parent;
        uiParent.GetNodeOrNull<Control>("BetterMultiplayerTradeOverlay")?.QueueFree();
        _ = new TradeOverlay(parent, location);
    }

    private void OnStateChanged()
    {
        if (_closed)
            return;

        bool confirmAfterSync = false;
        TradeSessionSnapshot? session = TradeStateStore.CurrentSession;
        if (session?.Status == TradeSessionStatus.Active &&
            session.Location == _location &&
            session.Contains(TradeNetwork.LocalPlayerId))
        {
            TradeOffer authoritative = session.OfferFor(TradeNetwork.LocalPlayerId);
            if (_draftSessionId != session.SessionId)
            {
                _draftSessionId = session.SessionId;
                _draft = authoritative.Clone();
                _offerUpdatePending = false;
                _selectionType = null;
            }
            else if (_offerUpdatePending)
            {
                if (OffersEqual(_draft, authoritative))
                {
                    _draft = authoritative.Clone();
                    _offerUpdatePending = false;
                    confirmAfterSync = _confirmAfterOfferSync;
                    _confirmAfterOfferSync = false;
                }
            }
            else if (_selectionType is null)
            {
                _draft = authoritative.Clone();
            }
        }
        else
        {
            CancelQueuedOffer();
            _offerUpdatePending = false;
            _confirmAfterOfferSync = false;
            _selectionType = null;
        }

        if (_location == TradeLocation.RestSite &&
            session?.Location == _location &&
            session.Status == TradeSessionStatus.Committed)
        {
            Close();
            return;
        }

        if (confirmAfterSync && session is not null)
        {
            SendConfirmation(session, confirmed: true);
            return;
        }

        Render();
    }

    private void SyncDraft()
    {
        TradeSessionSnapshot? session = TradeStateStore.CurrentSession;
        if (session?.Status != TradeSessionStatus.Active ||
            session.Location != _location ||
            !session.Contains(TradeNetwork.LocalPlayerId))
            return;

        _draftSessionId = session.SessionId;
        _draft = session.OfferFor(TradeNetwork.LocalPlayerId).Clone();
    }

    private void Render()
    {
        ClearBody();
        _confirmButton = null;
        _goldInput = null;

        ulong localId = TradeNetwork.LocalPlayerId;
        if (_location == TradeLocation.RestSite && TradeUsageTracker.HasUsed(localId))
        {
            RenderCompleted();
            return;
        }

        TradeSessionSnapshot? session = TradeStateStore.CurrentSession;
        if (session is null || session.Location != _location || !session.Contains(localId))
        {
            RenderPlayerList();
            return;
        }

        if (session.Status == TradeSessionStatus.Active && _selectionType is not null)
        {
            RenderSelection(session, _selectionType.Value);
            return;
        }

        switch (session.Status)
        {
            case TradeSessionStatus.Pending:
                RenderPending(session);
                break;
            case TradeSessionStatus.Active:
                RenderActive(session);
                break;
            case TradeSessionStatus.Committing:
                RenderCommitting(session);
                break;
            case TradeSessionStatus.Committed:
                RenderCompleted();
                break;
            case TradeSessionStatus.Canceled:
                RenderCanceled();
                break;
        }
    }

    private void RenderPlayerList()
    {
        _body.AddChild(UiFactory.Label(ModText.Get(TextKey.SelectTradePartner), 25));

        ScrollContainer scroll = new()
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        GridContainer grid = new()
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 12);
        scroll.AddChild(grid);
        _body.AddChild(scroll);

        RunState? state = RunManager.Instance.State;
        if (state is null)
        {
            SetStatus(ModText.Get(TextKey.RunUnavailable), error: true);
            return;
        }

        int playerCount = 0;
        foreach (Player player in state.Players.Where(player => player.NetId != TradeNetwork.LocalPlayerId))
        {
            playerCount++;
            bool available = TradeStateStore.IsAvailable(player.NetId, _location);
            bool used = _location == TradeLocation.RestSite && TradeUsageTracker.HasUsed(player.NetId);

            PanelContainer band = UiFactory.Band();
            band.CustomMinimumSize = new Vector2(690, 112);
            HBoxContainer row = new();
            row.AddThemeConstantOverride("separation", 14);
            band.AddChild(row);
            row.AddChild(CreateIcon(_location == TradeLocation.RestSite
                ? TradeAssets.RestTradeIcon
                : TradeAssets.GoldTradeIcon, new Vector2(96, 72)));

            VBoxContainer labels = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            labels.AddChild(UiFactory.Label(PlayerName(player.NetId), 22));
            labels.AddChild(UiFactory.Label(
                ModText.Get(used
                    ? TextKey.AlreadyTradedHere
                    : available
                        ? TextKey.ReadyToTrade
                        : TextKey.NotInTradeScreen),
                17,
                available && !used ? UiFactory.Good : UiFactory.TextMuted));
            row.AddChild(labels);

            Button invite = UiFactory.Button(ModText.Get(TextKey.Trade), () =>
            {
                TradeNetwork.SendRequest(new InviteRequest { TargetId = player.NetId });
                SetStatus(ModText.Get(TextKey.PlayerInvited, PlayerName(player.NetId)), error: false);
            }, primary: true);
            invite.CustomMinimumSize = new Vector2(130, 54);
            invite.Disabled = !available || used;
            row.AddChild(invite);
            grid.AddChild(band);
        }

        if (playerCount == 0)
            grid.AddChild(UiFactory.Label(ModText.Get(TextKey.NoOtherPlayers), 20, UiFactory.TextMuted));

        string error = TradeStateStore.LastError;
        SetStatus(
            error.Length > 0 ? error : ModText.Get(TextKey.WaitingForPlayers),
            error.Length > 0);
    }

    private void RenderPending(TradeSessionSnapshot session)
    {
        ulong localId = TradeNetwork.LocalPlayerId;
        ulong otherId = session.OtherPlayer(localId);
        bool isRecipient = session.PlayerB == localId;

        CenterContainer center = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        VBoxContainer panel = new();
        panel.CustomMinimumSize = new Vector2(760, 0);
        panel.Alignment = BoxContainer.AlignmentMode.Center;
        panel.AddThemeConstantOverride("separation", 24);
        panel.AddChild(CreateIcon(_location == TradeLocation.RestSite
            ? TradeAssets.RestTradeIcon
            : TradeAssets.GoldTradeIcon, new Vector2(230, 172)));
        panel.AddChild(UiFactory.Label(
            ModText.Get(
                isRecipient ? TextKey.TradeInviteReceived : TextKey.WaitingForInviteResponse,
                PlayerName(otherId)),
            28));

        HBoxContainer actions = new() { Alignment = BoxContainer.AlignmentMode.Center };
        actions.AddThemeConstantOverride("separation", 14);
        if (isRecipient)
        {
            actions.AddChild(UiFactory.Button(ModText.Get(TextKey.Decline), () =>
                TradeNetwork.SendRequest(new InviteResponseRequest
                {
                    SessionId = session.SessionId,
                    Accepted = false,
                    ReportedGold = LocalGold()
                }), danger: true));
            actions.AddChild(UiFactory.Button(ModText.Get(TextKey.Accept), () =>
                TradeNetwork.SendRequest(new InviteResponseRequest
                {
                    SessionId = session.SessionId,
                    Accepted = true,
                    ReportedGold = LocalGold()
                }), primary: true));
        }
        else
        {
            actions.AddChild(UiFactory.Button(ModText.Get(TextKey.CancelInvite), () => Cancel(session), danger: true));
        }
        panel.AddChild(actions);
        center.AddChild(panel);
        _body.AddChild(center);
        SetStatus(TradeStateStore.LastError, TradeStateStore.LastError.Length > 0);
    }

    private void RenderActive(TradeSessionSnapshot session)
    {
        ulong localId = TradeNetwork.LocalPlayerId;
        Player? localPlayer = RunManager.Instance.State?.GetPlayer(localId);
        Player? otherPlayer = RunManager.Instance.State?.GetPlayer(session.OtherPlayer(localId));
        if (localPlayer is null || otherPlayer is null)
        {
            SetStatus(ModText.Get(TextKey.TradePlayerLeft), error: true);
            return;
        }

        VBoxContainer board = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        board.AddThemeConstantOverride("separation", 5);
        board.AddChild(CreateOfferPanel(
            localPlayer,
            _draft,
            session.GoldFor(localPlayer.NetId),
            isLocal: true,
            confirmed: session.IsConfirmed(localId),
            locked: session.IsConfirmed(localId) || _offerUpdatePending));
        board.AddChild(CreateExchangeDivider());
        board.AddChild(CreateOfferPanel(
            otherPlayer,
            session.OfferFor(otherPlayer.NetId),
            session.GoldFor(otherPlayer.NetId),
            isLocal: false,
            confirmed: session.IsConfirmed(otherPlayer.NetId),
            locked: true));
        _body.AddChild(board);

        HBoxContainer actions = new() { Alignment = BoxContainer.AlignmentMode.End };
        actions.AddThemeConstantOverride("separation", 12);
        actions.AddChild(UiFactory.Button(ModText.Get(TextKey.CancelTrade), () => Cancel(session), danger: true));

        bool localConfirmed = session.IsConfirmed(localId);
        _confirmButton = UiFactory.Button(
            ModText.Get(localConfirmed ? TextKey.WithdrawConfirmation : TextKey.ConfirmTrade),
            () => SetConfirmation(session, !localConfirmed),
            primary: !localConfirmed);
        _confirmButton.CustomMinimumSize = new Vector2(160, 52);
        _confirmButton.Disabled = _offerUpdatePending;
        actions.AddChild(_confirmButton);
        _body.AddChild(actions);

        string stateText = localConfirmed
            ? ModText.Get(session.IsOtherConfirmed(localId)
                ? TextKey.BothConfirmed
                : TextKey.WaitingForOtherConfirmation)
            : ModText.Get(_offerUpdatePending ? TextKey.SyncingOffer : TextKey.WaitingForConfirmations);
        string error = TradeStateStore.LastError;
        SetStatus(error.Length > 0 ? error : stateText, error.Length > 0);
    }

    private Control CreateOfferPanel(
        Player player,
        TradeOffer offer,
        int availableGold,
        bool isLocal,
        bool confirmed,
        bool locked)
    {
        MarginContainer panel = new();
        panel.CustomMinimumSize = new Vector2(0, _location == TradeLocation.RestSite ? 250 : 220);
        panel.AddThemeConstantOverride("margin_left", 18);
        panel.AddThemeConstantOverride("margin_right", 18);
        panel.AddThemeConstantOverride("margin_top", 10);
        panel.AddThemeConstantOverride("margin_bottom", 10);
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 16);
        panel.AddChild(row);

        VBoxContainer identity = new() { CustomMinimumSize = new Vector2(210, 0) };
        identity.AddChild(UiFactory.Label(isLocal ? ModText.Get(TextKey.YourOffer) : PlayerName(player.NetId), 24));
        identity.AddChild(UiFactory.Label(
            ModText.Get(confirmed ? TextKey.Confirmed : TextKey.NotConfirmed),
            18,
            confirmed ? UiFactory.Good : UiFactory.TextMuted));
        if (_location == TradeLocation.Merchant)
            identity.AddChild(UiFactory.Label(ModText.Get(TextKey.GoldOnHand, availableGold), 17, UiFactory.TextMuted));
        row.AddChild(identity);

        Control offerContent = _location == TradeLocation.RestSite
            ? CreateRestOffer(player, offer, isLocal, locked)
            : CreateGoldOffer(player, offer, isLocal, locked);
        offerContent.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(offerContent);

        Label state = UiFactory.Label(confirmed ? "✓" : "…", 44, confirmed ? UiFactory.Good : UiFactory.TextMuted);
        state.HorizontalAlignment = HorizontalAlignment.Center;
        state.CustomMinimumSize = new Vector2(72, 0);
        row.AddChild(state);
        return panel;
    }

    private Control CreateRestOffer(Player player, TradeOffer offer, bool isLocal, bool locked)
    {
        HBoxContainer content = new();
        content.AddThemeConstantOverride("separation", 18);

        VBoxContainer cards = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cards.AddChild(UiFactory.Label(
            ModText.Get(TextKey.CardsCount, offer.CardIndices.Count, TradeValidator.MaxCards),
            17,
            UiFactory.TextMuted));
        HBoxContainer cardSlots = new();
        cardSlots.AddThemeConstantOverride("separation", 10);
        for (int slot = 0; slot < TradeValidator.MaxCards; slot++)
        {
            int? index = slot < offer.CardIndices.Count ? offer.CardIndices[slot] : null;
            CardModel? card = index.HasValue && index.Value >= 0 && index.Value < player.Deck.Cards.Count
                ? player.Deck.Cards[index.Value]
                : null;
            cardSlots.AddChild(CreateItemTile(
                card is null ? ModText.Get(TextKey.ChooseCard) : card.Title,
                card is null ? null : TryGetTexture(() => card.Portrait),
                new Vector2(170, 190),
                isLocal && !locked ? () => OpenSelection(OfferItemType.Card) : null,
                selected: card is not null));
        }
        cards.AddChild(cardSlots);
        content.AddChild(cards);

        VBoxContainer extras = new() { CustomMinimumSize = new Vector2(180, 0) };
        int? relicIndex = offer.RelicIndices.Count > 0 ? offer.RelicIndices[0] : null;
        RelicModel? relic = relicIndex.HasValue && relicIndex.Value >= 0 && relicIndex.Value < player.Relics.Count
            ? player.Relics[relicIndex.Value]
            : null;
        extras.AddChild(UiFactory.Label(ModText.Get(TextKey.Relic), 17, UiFactory.TextMuted));
        extras.AddChild(CreateItemTile(
            relic is null ? ModText.Get(TextKey.ChooseRelic) : relic.Title.GetFormattedText(),
            relic is null ? null : TryGetTexture(() => relic.Icon),
            new Vector2(175, 82),
            isLocal && !locked ? () => OpenSelection(OfferItemType.Relic) : null,
            selected: relic is not null));

        int? potionIndex = offer.PotionSlotIndices.Count > 0 ? offer.PotionSlotIndices[0] : null;
        PotionModel? potion = potionIndex.HasValue ? player.GetPotionAtSlotIndex(potionIndex.Value) : null;
        extras.AddChild(UiFactory.Label(ModText.Get(TextKey.Potion), 17, UiFactory.TextMuted));
        extras.AddChild(CreateItemTile(
            potion is null ? ModText.Get(TextKey.ChoosePotion) : potion.Title.GetFormattedText(),
            potion is null ? null : TryGetTexture(() => potion.Image),
            new Vector2(175, 82),
            isLocal && !locked ? () => OpenSelection(OfferItemType.Potion) : null,
            selected: potion is not null));
        content.AddChild(extras);
        return content;
    }

    private Control CreateGoldOffer(Player player, TradeOffer offer, bool isLocal, bool locked)
    {
        HBoxContainer content = new() { Alignment = BoxContainer.AlignmentMode.Center };
        content.AddThemeConstantOverride("separation", 28);
        content.AddChild(CreateIcon(TradeAssets.GoldTradeIcon, new Vector2(210, 158)));

        VBoxContainer amount = new() { CustomMinimumSize = new Vector2(330, 0) };
        amount.AddChild(UiFactory.Label(
            ModText.Get(isLocal ? TextKey.GoldYouOffer : TextKey.GoldTheyOffer),
            20,
            UiFactory.TextMuted));
        if (isLocal)
        {
            LineEdit gold = UiFactory.LineEdit(ModText.Get(TextKey.GoldAmountPlaceholder), maxLength: 10);
            gold.Text = _draft.Gold.ToString();
            gold.Editable = !locked;
            gold.CustomMinimumSize = new Vector2(300, 74);
            gold.AddThemeFontSizeOverride("font_size", 34);
            gold.Alignment = HorizontalAlignment.Center;
            if (!locked)
            {
                gold.TextChanged += text =>
                {
                    if (text.Length > 0 && text.All(char.IsDigit))
                        return;

                    int caret = gold.CaretColumn;
                    string digits = new(text.Where(char.IsDigit).ToArray());
                    if (gold.Text != digits)
                    {
                        gold.Text = digits;
                        gold.CaretColumn = Math.Min(caret, digits.Length);
                    }
                };
            }
            _goldInput = gold;
            amount.AddChild(gold);
        }
        else
        {
            Label value = UiFactory.Label(offer.Gold.ToString(), 46, UiFactory.Accent);
            value.CustomMinimumSize = new Vector2(300, 74);
            amount.AddChild(value);
        }
        content.AddChild(amount);
        return content;
    }

    private Control CreateExchangeDivider()
    {
        HBoxContainer divider = new() { Alignment = BoxContainer.AlignmentMode.Center };
        divider.CustomMinimumSize = new Vector2(0, 62);
        HSeparator left = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        HSeparator right = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        divider.AddChild(left);
        divider.AddChild(CreateIcon(
            _location == TradeLocation.RestSite ? TradeAssets.RestTradeIcon : TradeAssets.GoldTradeIcon,
            new Vector2(92, 58)));
        divider.AddChild(right);
        return divider;
    }

    private void OpenSelection(OfferItemType type)
    {
        _selectionType = type;
        _selectionIndices = type switch
        {
            OfferItemType.Card => [.. _draft.CardIndices],
            OfferItemType.Relic => [.. _draft.RelicIndices],
            OfferItemType.Potion => [.. _draft.PotionSlotIndices],
            _ => []
        };
        Render();
    }

    private void RenderSelection(TradeSessionSnapshot session, OfferItemType type)
    {
        Player? player = RunManager.Instance.State?.GetPlayer(TradeNetwork.LocalPlayerId);
        if (player is null)
        {
            SetStatus(ModText.Get(TextKey.PlayerUnavailable), error: true);
            return;
        }

        int limit = SelectionLimit(type);
        string title = type switch
        {
            OfferItemType.Card => ModText.Get(TextKey.ChooseCard),
            OfferItemType.Relic => ModText.Get(TextKey.ChooseRelic),
            OfferItemType.Potion => ModText.Get(TextKey.ChoosePotion),
            _ => ModText.Get(TextKey.ChooseItem)
        };
        HBoxContainer header = new();
        Label heading = UiFactory.Label(
            ModText.Get(TextKey.SelectionCount, title, _selectionIndices.Count, limit),
            25);
        heading.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(heading);
        _body.AddChild(header);

        ScrollContainer scroll = new()
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        GridContainer grid = new()
        {
            Columns = type == OfferItemType.Card ? 6 : 7,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        scroll.AddChild(grid);
        _body.AddChild(scroll);

        switch (type)
        {
            case OfferItemType.Card:
                for (int i = 0; i < player.Deck.Cards.Count; i++)
                {
                    int index = i;
                    CardModel card = player.Deck.Cards[index];
                    grid.AddChild(CreateSelectionTile(
                        index,
                        card.Title,
                        TryGetTexture(() => card.Portrait),
                        TradeValidator.CanTradeCard(card),
                        new Vector2(210, 230)));
                }
                break;
            case OfferItemType.Relic:
                for (int i = 0; i < player.Relics.Count; i++)
                {
                    int index = i;
                    RelicModel relic = player.Relics[index];
                    grid.AddChild(CreateSelectionTile(
                        index,
                        relic.Title.GetFormattedText(),
                        TryGetTexture(() => relic.Icon),
                        TradeValidator.CanTradeRelic(relic),
                        new Vector2(180, 160)));
                }
                break;
            case OfferItemType.Potion:
                for (int i = 0; i < player.PotionSlots.Count; i++)
                {
                    int index = i;
                    PotionModel? potion = player.PotionSlots[index];
                    if (potion is null)
                        continue;
                    grid.AddChild(CreateSelectionTile(
                        index,
                        potion.Title.GetFormattedText(),
                        TryGetTexture(() => potion.Image),
                        GameApiCompatibility.CanRemovePotions(player),
                        new Vector2(180, 160)));
                }
                break;
        }

        HBoxContainer actions = new() { Alignment = BoxContainer.AlignmentMode.End };
        actions.AddThemeConstantOverride("separation", 12);
        actions.AddChild(UiFactory.Button(ModText.Get(TextKey.Cancel), CancelSelection));
        Button confirm = UiFactory.Button(ModText.Get(TextKey.ConfirmSelection), ConfirmSelection, primary: true);
        confirm.CustomMinimumSize = new Vector2(160, 52);
        actions.AddChild(confirm);
        _body.AddChild(actions);
        SetStatus(TradeStateStore.LastError, TradeStateStore.LastError.Length > 0);
    }

    private Button CreateSelectionTile(
        int index,
        string title,
        Texture2D? texture,
        bool tradable,
        Vector2 size)
    {
        bool selected = _selectionIndices.Contains(index);
        Button tile = CreateItemTile(title, texture, size, () => ToggleSelection(index), selected);
        tile.Disabled = !tradable;
        if (!tradable)
            tile.TooltipText = ModText.Get(TextKey.ItemCannotBeTraded, title);
        return tile;
    }

    private void ToggleSelection(int index)
    {
        if (_selectionIndices.Remove(index))
        {
            Render();
            return;
        }

        int limit = SelectionLimit(_selectionType ?? OfferItemType.Card);
        if (limit == 1)
            _selectionIndices.Clear();
        else if (_selectionIndices.Count >= limit)
        {
            SetStatus(ModText.Get(TextKey.SelectionLimit, limit), error: true);
            return;
        }
        _selectionIndices.Add(index);
        _selectionIndices.Sort();
        Render();
    }

    private void ConfirmSelection()
    {
        if (_selectionType is null)
            return;

        List<int> target = _selectionType.Value switch
        {
            OfferItemType.Card => _draft.CardIndices,
            OfferItemType.Relic => _draft.RelicIndices,
            OfferItemType.Potion => _draft.PotionSlotIndices,
            _ => throw new ArgumentOutOfRangeException()
        };
        target.Clear();
        target.AddRange(_selectionIndices);
        _draft = _draft.Normalized();
        _selectionType = null;
        QueueDraftUpdate();
        Render();
    }

    private void CancelSelection()
    {
        _selectionType = null;
        _selectionIndices.Clear();
        Render();
    }

    private void HandleBack()
    {
        if (_selectionType is not null)
        {
            CancelSelection();
            return;
        }

        Close();
    }

    private static int SelectionLimit(OfferItemType type) => type switch
    {
        OfferItemType.Card => TradeValidator.MaxCards,
        OfferItemType.Relic => TradeValidator.MaxRelics,
        OfferItemType.Potion => TradeValidator.MaxPotions,
        _ => 0
    };

    private static Button CreateItemTile(
        string title,
        Texture2D? texture,
        Vector2 size,
        Action? onPressed,
        bool selected)
    {
        Button tile = new()
        {
            Text = title,
            Icon = texture,
            CustomMinimumSize = size,
            ExpandIcon = true,
            IconAlignment = HorizontalAlignment.Center,
            VerticalIconAlignment = VerticalAlignment.Top,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            TooltipText = title,
            FocusMode = onPressed is null ? Control.FocusModeEnum.None : Control.FocusModeEnum.All,
            MouseFilter = onPressed is null ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop
        };
        tile.AddThemeFontSizeOverride("font_size", 16);
        Color border = selected ? UiFactory.Accent : UiFactory.Border;
        Color background = selected ? SelectedSurface : SlotSurface;
        tile.AddThemeStyleboxOverride("normal", UiFactory.PanelStyle(background, border, selected ? 3 : 1, 5));
        tile.AddThemeStyleboxOverride("hover", UiFactory.PanelStyle(background.Lightened(0.08f), UiFactory.Accent, 2, 5));
        tile.AddThemeStyleboxOverride("pressed", UiFactory.PanelStyle(background.Darkened(0.08f), UiFactory.Accent, 3, 5));
        if (onPressed is not null)
            UiFactory.AttachNativeInput(tile, onPressed);
        return tile;
    }

    private static TextureRect CreateIcon(Texture2D texture, Vector2 size)
    {
        return new TextureRect
        {
            Texture = texture,
            CustomMinimumSize = size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
    }

    private void RenderCommitting(TradeSessionSnapshot session)
    {
        CenterContainer center = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        VBoxContainer content = new() { Alignment = BoxContainer.AlignmentMode.Center };
        content.AddThemeConstantOverride("separation", 18);
        content.AddChild(CreateIcon(
            _location == TradeLocation.RestSite ? TradeAssets.RestTradeIcon : TradeAssets.GoldTradeIcon,
            new Vector2(260, 195)));
        content.AddChild(UiFactory.Label(ModText.Get(TextKey.SubmittingTrade), 28));
        content.AddChild(UiFactory.Label(ModText.Get(TextKey.TransactionId, session.SessionId), 16, UiFactory.TextMuted));
        center.AddChild(content);
        _body.AddChild(center);
        SetStatus(ModText.Get(TextKey.DoNotLeaveRun), error: false);
    }

    private void RenderCompleted()
    {
        bool restSite = _location == TradeLocation.RestSite;
        CenterContainer center = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        VBoxContainer content = new() { Alignment = BoxContainer.AlignmentMode.Center };
        content.AddThemeConstantOverride("separation", 18);
        content.AddChild(CreateIcon(
            restSite ? TradeAssets.RestTradeIcon : TradeAssets.GoldTradeIcon,
            new Vector2(240, 180)));
        content.AddChild(UiFactory.Label(ModText.Get(TextKey.TradeComplete), 30, UiFactory.Good));
        if (restSite)
            content.AddChild(UiFactory.Label(ModText.Get(TextKey.RestActionsRemain), 20, UiFactory.TextMuted));
        HBoxContainer actions = new() { Alignment = BoxContainer.AlignmentMode.Center };
        if (!restSite)
            actions.AddChild(UiFactory.Button(ModText.Get(TextKey.TradeAgain), TradeStateStore.ClearSession, primary: true));
        actions.AddChild(UiFactory.Button(
            ModText.Get(restSite ? TextKey.BackToRestSite : TextKey.Close),
            Close,
            primary: restSite));
        content.AddChild(actions);
        center.AddChild(content);
        _body.AddChild(center);
        SetStatus(string.Empty, error: false);
    }

    private void RenderCanceled()
    {
        CenterContainer center = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        VBoxContainer content = new() { Alignment = BoxContainer.AlignmentMode.Center };
        content.AddThemeConstantOverride("separation", 18);
        content.AddChild(UiFactory.Label(ModText.Get(TextKey.TradeCanceled), 28));
        content.AddChild(UiFactory.Button(ModText.Get(TextKey.BackToPlayerList), TradeStateStore.ClearSession, primary: true));
        center.AddChild(content);
        _body.AddChild(center);
        SetStatus(TradeStateStore.LastError, TradeStateStore.LastError.Length > 0);
    }

    private void SetConfirmation(TradeSessionSnapshot session, bool confirmed)
    {
        if (!confirmed)
        {
            SendConfirmation(session, confirmed: false);
            return;
        }

        if (_location == TradeLocation.Merchant)
        {
            Player? player = RunManager.Instance.State?.GetPlayer(TradeNetwork.LocalPlayerId);
            if (player is null)
            {
                SetStatus(ModText.Get(TextKey.PlayerUnavailable), error: true);
                return;
            }
            if (_goldInput is null)
            {
                SetStatus(ModText.Get(TextKey.GoldInputUnavailable), error: true);
                return;
            }
            if (!TradeGoldInput.TryParse(_goldInput.Text, player.Gold, out int gold, out string parseError))
            {
                SetStatus(parseError, error: true);
                _goldInput.GrabFocus();
                return;
            }
            _draft.Gold = gold;
        }

        TradeOffer authoritative = session.OfferFor(TradeNetwork.LocalPlayerId);
        if (!OffersEqual(_draft, authoritative))
        {
            CancelQueuedOffer();
            _offerUpdatePending = true;
            _confirmAfterOfferSync = true;
            if (_confirmButton is not null)
                _confirmButton.Disabled = true;
            SetStatus(ModText.Get(TextKey.SubmittingOffer), error: false);
            TradeNetwork.SendRequest(new OfferUpdateRequest
            {
                SessionId = session.SessionId,
                Offer = _draft.Clone(),
                ReportedGold = LocalGold()
            });
            return;
        }

        SendConfirmation(session, confirmed: true);
    }

    private static void SendConfirmation(TradeSessionSnapshot session, bool confirmed)
    {
        TradeNetwork.SendRequest(new ConfirmRequest
        {
            SessionId = session.SessionId,
            Revision = session.Revision,
            Confirmed = confirmed,
            ReportedGold = LocalGold()
        });
    }

    private void QueueDraftUpdate()
    {
        TradeSessionSnapshot? session = TradeStateStore.CurrentSession;
        if (session?.Status != TradeSessionStatus.Active ||
            session.Location != _location ||
            session.SessionId != _draftSessionId)
            return;

        CancelQueuedOffer();
        _offerUpdatePending = true;
        if (_confirmButton is not null)
            _confirmButton.Disabled = true;

        CancellationTokenSource source = new();
        _offerSendCancellation = source;
        TaskHelper.RunSafely(SendDraftAfterDelay(source, session.SessionId));
        SetStatus(ModText.Get(TextKey.SyncingOffer), error: false);
    }

    private async Task SendDraftAfterDelay(CancellationTokenSource source, ulong sessionId)
    {
        try
        {
            await Task.Delay(120, source.Token);
            if (!_closed && !source.IsCancellationRequested)
            {
                TradeNetwork.SendRequest(new OfferUpdateRequest
                {
                    SessionId = sessionId,
                    Offer = _draft.Clone(),
                    ReportedGold = LocalGold()
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_offerSendCancellation, source))
                _offerSendCancellation = null;
            source.Dispose();
        }
    }

    private void Cancel(TradeSessionSnapshot session)
    {
        TradeNetwork.SendRequest(new CancelTradeRequest { SessionId = session.SessionId });
    }

    private static Texture2D? TryGetTexture(Func<Texture2D> getTexture)
    {
        try
        {
            return getTexture();
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Warn($"Reading a trade item icon failed: {ex.Message}");
            return null;
        }
    }

    private static string PlayerName(ulong playerId)
    {
        try
        {
            return PlatformUtil.GetPlayerNameRaw(RunManager.Instance.NetService.Platform, playerId);
        }
        catch
        {
            return ModText.Get(TextKey.PlayerFallback, playerId);
        }
    }

    private static bool OffersEqual(TradeOffer left, TradeOffer right)
    {
        TradeOffer a = left.Normalized();
        TradeOffer b = right.Normalized();
        return a.Gold == b.Gold &&
               a.CardIndices.SequenceEqual(b.CardIndices) &&
               a.RelicIndices.SequenceEqual(b.RelicIndices) &&
               a.PotionSlotIndices.SequenceEqual(b.PotionSlotIndices);
    }

    private void SetStatus(string text, bool error)
    {
        if (!GodotObject.IsInstanceValid(_status))
            return;
        _status.Text = ModText.Resolve(text);
        _status.AddThemeColorOverride("font_color", error ? UiFactory.Danger : UiFactory.TextMuted);
    }

    private void ClearBody()
    {
        foreach (Node child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void CancelQueuedOffer()
    {
        _offerSendCancellation?.Cancel();
        _offerSendCancellation = null;
    }

    private static bool CanUseNetwork()
    {
        return RunManager.Instance.IsInProgress && RunManager.Instance.NetService.IsConnected;
    }

    private void Close()
    {
        Shutdown();
        if (GodotObject.IsInstanceValid(_backButton))
            _backButton.Disable();
        if (GodotObject.IsInstanceValid(_root))
            _root.QueueFree();
    }

    private void OnSourceTreeExiting() => Close();

    private void OnTreeExiting() => Shutdown();

    private void Shutdown()
    {
        if (_closed)
            return;

        _closed = true;
        if (GodotObject.IsInstanceValid(_sourceParent))
            _sourceParent.TreeExiting -= OnSourceTreeExiting;
        CancelQueuedOffer();
        TradeStateStore.Changed -= OnStateChanged;
        if (_location == TradeLocation.RestSite)
            TradeRestSiteFlow.Complete(TradeNetwork.LocalPlayerId, success: false);
        if (_announced && CanUseNetwork())
        {
            _announced = false;
            try
            {
                TradeNetwork.SendRequest(new AvailabilityRequest
                {
                    Available = false,
                    Location = _location,
                    ReportedGold = LocalGold()
                });
            }
            catch (Exception ex)
            {
                BetterMultiplayerMod.Logger.Warn($"Withdrawing trade availability failed: {ex.Message}");
            }
        }
    }

    private static int LocalGold()
    {
        Player? player = RunManager.Instance.State?.GetPlayer(TradeNetwork.LocalPlayerId);
        return player?.Gold ?? 0;
    }
}
