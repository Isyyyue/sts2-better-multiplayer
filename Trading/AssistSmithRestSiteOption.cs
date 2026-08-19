using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using BetterMultiplayer.Trading.Messages;
using BetterMultiplayer.Localization;

namespace BetterMultiplayer.Trading;

internal sealed class AssistSmithRestSiteOption(Player owner) : CustomRestSiteOption(owner)
{
    private sealed record TargetControlState(
        Control Hitbox,
        Control.FocusModeEnum FocusMode,
        NodePath FocusNeighborTop,
        NodePath FocusNeighborBottom,
        NodePath FocusNeighborLeft,
        NodePath FocusNeighborRight,
        IReadOnlyList<TargetMouseFilterState> MouseFilters);

    private sealed record TargetMouseFilterState(
        Control Control,
        Control.MouseFilterEnum OriginalMouseFilter,
        Control.MouseFilterEnum ActiveMouseFilter);

    private readonly Player _owner = owner;
    private Player? _target;
    private CardModel[] _selection = [];

    public override string OptionId => "BETTER_MULTIPLAYER_ASSIST_SMITH";

    public override string CustomIconPath => ImageHelper.GetImagePath("ui/rest_site/option_smith.png");

    public override IEnumerable<string> AssetPaths => NCardSmithVfx.AssetPaths;

    public override bool IsEnabled =>
        _owner.RunState.Players.Any(player => player != _owner && player.Deck.UpgradableCardCount > 0);

    public override async Task<bool> OnSelect()
    {
        BetterMultiplayerMod.Logger.Info(
            $"Assist Smith option selected: player={_owner.NetId}");
        AssistSmithCoordinator.Register(_owner.NetId);
        Task<AssistSmithResult> resultTask = AssistSmithFlow.WaitForResult(_owner.NetId);

        if (LocalContext.IsMe(_owner))
            await CollectLocalSelection();

        AssistSmithResult result = await resultTask;
        if (!result.Success)
        {
            if (!string.IsNullOrWhiteSpace(result.Error) && LocalContext.IsMe(_owner))
                BetterMultiplayerMod.Logger.Warn($"Assist Smith failed: {ModText.Resolve(result.Error)}");
            return false;
        }

        string error = string.Empty;
        Player? target = _owner.RunState.GetPlayer(result.TargetId);
        if (target is null ||
            !AssistSmithSelection.TryResolve(
                _owner,
                target,
                result.CardIndex,
                result.CardId,
                result.UpgradeLevel,
                out CardModel? card,
                out error))
        {
            BetterMultiplayerMod.Logger.Error($"Synchronizing Assist Smith failed: {ModText.Resolve(error)}");
            return false;
        }

        _target = target;
        _selection = [card!];
        CardCmd.Upgrade(card!, CardPreviewStyle.None);
        await Hook.AfterRestSiteSmith(target.RunState, target);
        return true;
    }

    public override async Task DoLocalPostSelectVfx(CancellationToken ct = default)
    {
        if (_selection.Length > 0)
            NRun.Instance?.GlobalUi.CardPreviewContainer.AddChildSafely(NCardSmithVfx.Create(_selection));
        await Cmd.CustomScaledWait(1f, 2f, ignoreCombatEnd: false, ct);
    }

    public override Task DoRemotePostSelectVfx()
    {
        if (_target is null)
            return Task.CompletedTask;

        NRestSiteCharacter? character = NRestSiteRoom.Instance?.GetCharacterForPlayer(_target);
        NCardSmithVfx? vfx = NCardSmithVfx.Create();
        if (vfx is null)
            return Task.CompletedTask;
        character?.AddChildSafely(vfx);
        vfx.Position = Vector2.Zero;
        return Task.CompletedTask;
    }

    private async Task CollectLocalSelection()
    {
        try
        {
            Player? target = await SelectTarget();
            CardModel? card = target is null ? null : await SelectCard(target);
            if (target is null || card is null)
            {
                SendCanceled();
                return;
            }

            int cardIndex = target.Deck.Cards.ToList().IndexOf(card);
            if (cardIndex < 0)
            {
                SendCanceled();
                return;
            }

            TradeNetwork.SendRequest(new AssistSmithRequest
            {
                TargetId = target.NetId,
                CardIndex = cardIndex,
                CardId = card.Id.ToString(),
                UpgradeLevel = card.CurrentUpgradeLevel
            });
        }
        catch (Exception ex)
        {
            BetterMultiplayerMod.Logger.Error($"Selecting an Assist Smith target failed: {ex}");
            SendCanceled();
        }
    }

    private async Task<Player?> SelectTarget()
    {
        NRestSiteRoom? room = NRestSiteRoom.Instance;
        if (room is null)
            return null;

        NRestSiteButton? button = room.GetButtonForOption(this);
        NTargetManager? targetManager = NTargetManager.Instance;
        if (button is null || targetManager is null)
            return null;

        room.AnimateDescriptionDown();
        Vector2 startPosition = button.GlobalPosition + button.Size / 2f;
        bool usingController = GameApiCompatibility.IsUsingController(NControllerManager.Instance);
        List<NRestSiteCharacter> targets = room.Characters
            .Where(character => IsValidTarget(character.Player))
            .ToList();
        List<TargetControlState> controlStates = [];
        bool selectionFinished = false;
        try
        {
            targetManager.StartTargeting(
                TargetType.AnyPlayer,
                startPosition,
                usingController ? TargetMode.Controller : TargetMode.ClickMouseToTarget,
                ShouldCancelTargeting,
                AllowHoveringNode);
            PrepareTargetControls(targets, controlStates);
            if (usingController)
                ConfigureControllerTargets(targets);
            Node? selectedNode = await targetManager.SelectionFinished();
            selectionFinished = true;
            return NodeToPlayer(selectedNode);
        }
        finally
        {
            try
            {
                if (!selectionFinished && targetManager.IsInSelection)
                    targetManager.CancelTargeting();
            }
            finally
            {
                RestoreTargetControls(controlStates);
                room.AnimateDescriptionUp();
            }
        }
    }

    private static async Task<CardModel?> SelectCard(Player target)
    {
        List<CardModel> cards = target.Deck.Cards.Where(card => card.IsUpgradable).ToList();
        if (cards.Count == 0)
            return null;

        CardSelectorPrefs prefs = new(CardSelectorPrefs.UpgradeSelectionPrompt, 1)
        {
            Cancelable = true,
            RequireManualConfirmation = true
        };
        NDeckUpgradeSelectScreen screen = NDeckUpgradeSelectScreen.ShowScreen(cards, prefs, target.RunState);
        return (await screen.CardsSelected()).FirstOrDefault();
    }

    private static void ConfigureControllerTargets(IReadOnlyList<NRestSiteCharacter> targets)
    {
        for (int index = 0; index < targets.Count; index++)
        {
            Control hitbox = targets[index].Hitbox;
            hitbox.SetFocusMode(Control.FocusModeEnum.All);
            hitbox.FocusNeighborTop = hitbox.GetPath();
            hitbox.FocusNeighborBottom = hitbox.GetPath();
            hitbox.FocusNeighborLeft = targets[(index - 1 + targets.Count) % targets.Count].Hitbox.GetPath();
            hitbox.FocusNeighborRight = targets[(index + 1) % targets.Count].Hitbox.GetPath();
        }
        targets.FirstOrDefault()?.Hitbox.TryGrabFocus();
    }

    private static void PrepareTargetControls(
        IEnumerable<NRestSiteCharacter> targets,
        ICollection<TargetControlState> states)
    {
        foreach (NRestSiteCharacter character in targets)
        {
            Control hitbox = character.Hitbox;
            IReadOnlyList<TargetMouseFilterState> mouseFilters = BuildMouseFilterStates(character, hitbox);
            states.Add(new TargetControlState(
                hitbox,
                hitbox.FocusMode,
                hitbox.FocusNeighborTop,
                hitbox.FocusNeighborBottom,
                hitbox.FocusNeighborLeft,
                hitbox.FocusNeighborRight,
                mouseFilters));

            foreach (TargetMouseFilterState mouseFilter in mouseFilters)
                mouseFilter.Control.MouseFilter = mouseFilter.ActiveMouseFilter;
        }
    }

    private static IReadOnlyList<TargetMouseFilterState> BuildMouseFilterStates(
        NRestSiteCharacter character,
        Control hitbox)
    {
        List<Control> controls = EnumerateDescendantControls(character).ToList();
        if (!controls.Any(control => control == hitbox))
            controls.Insert(0, hitbox);

        Rect2 hitboxRect = hitbox.GetGlobalRect();
        AssistSmithTargetControlInput[] inputs = controls
            .Select(control => new AssistSmithTargetControlInput(
                control == hitbox,
                hitboxRect.Intersects(control.GetGlobalRect(), includeBorders: true),
                control.MouseFilter))
            .ToArray();
        AssistSmithTargetControlPlan[] plan = AssistSmithTargetInputPolicy.BuildPlan(inputs);

        return controls
            .Select((control, index) => new TargetMouseFilterState(
                control,
                plan[index].OriginalMouseFilter,
                plan[index].ActiveMouseFilter))
            .Where(state => state.OriginalMouseFilter != state.ActiveMouseFilter)
            .ToArray();
    }

    private static IEnumerable<Control> EnumerateDescendantControls(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is Control control)
                yield return control;

            foreach (Control descendant in EnumerateDescendantControls(child))
                yield return descendant;
        }
    }

    private static void RestoreTargetControls(IEnumerable<TargetControlState> states)
    {
        foreach (TargetControlState state in states)
        {
            foreach (TargetMouseFilterState mouseFilter in state.MouseFilters)
            {
                if (GodotObject.IsInstanceValid(mouseFilter.Control) &&
                    AssistSmithTargetInputPolicy.ShouldRestore(
                        mouseFilter.OriginalMouseFilter,
                        mouseFilter.ActiveMouseFilter,
                        mouseFilter.Control.MouseFilter))
                {
                    mouseFilter.Control.MouseFilter = mouseFilter.OriginalMouseFilter;
                }
            }

            if (!GodotObject.IsInstanceValid(state.Hitbox))
                continue;

            state.Hitbox.FocusNeighborTop = state.FocusNeighborTop;
            state.Hitbox.FocusNeighborBottom = state.FocusNeighborBottom;
            state.Hitbox.FocusNeighborLeft = state.FocusNeighborLeft;
            state.Hitbox.FocusNeighborRight = state.FocusNeighborRight;
            state.Hitbox.FocusMode = state.FocusMode;
        }
    }

    private bool AllowHoveringNode(Node node) => IsValidTarget(NodeToPlayer(node));

    private bool IsValidTarget(Player? player) =>
        AssistSmithTargetPolicy.CanTarget(_owner, player);

    private static Player? NodeToPlayer(Node? node) => node switch
    {
        NMultiplayerPlayerState state => state.Player,
        NRestSiteCharacter character => character.Player,
        _ => null
    };

    private static bool ShouldCancelTargeting() =>
        NOverlayStack.Instance?.ScreenCount > 0 || NCapstoneContainer.Instance?.InUse == true;

    private static void SendCanceled() =>
        TradeNetwork.SendRequest(new AssistSmithRequest { Canceled = true, CardIndex = -1 });
}

internal static class AssistSmithTargetPolicy
{
    internal static bool CanTarget(Player owner, Player? candidate) =>
        candidate is not null && candidate != owner;
}

internal readonly record struct AssistSmithTargetControlInput(
    bool IsHitbox,
    bool IntersectsHitbox,
    Control.MouseFilterEnum OriginalMouseFilter);

internal readonly record struct AssistSmithTargetControlPlan(
    Control.MouseFilterEnum ActiveMouseFilter,
    Control.MouseFilterEnum OriginalMouseFilter);

internal static class AssistSmithTargetInputPolicy
{
    internal static AssistSmithTargetControlPlan[] BuildPlan(
        IReadOnlyList<AssistSmithTargetControlInput> controls)
    {
        AssistSmithTargetControlPlan[] plan = new AssistSmithTargetControlPlan[controls.Count];
        for (int index = 0; index < controls.Count; index++)
        {
            AssistSmithTargetControlInput control = controls[index];
            Control.MouseFilterEnum activeMouseFilter = control switch
            {
                { IsHitbox: true } => Control.MouseFilterEnum.Stop,
                { IntersectsHitbox: true } => Control.MouseFilterEnum.Ignore,
                _ => control.OriginalMouseFilter
            };
            plan[index] = new AssistSmithTargetControlPlan(
                activeMouseFilter,
                control.OriginalMouseFilter);
        }
        return plan;
    }

    internal static bool ShouldRestore(
        Control.MouseFilterEnum originalMouseFilter,
        Control.MouseFilterEnum activeMouseFilter,
        Control.MouseFilterEnum currentMouseFilter) =>
        originalMouseFilter != activeMouseFilter && currentMouseFilter == activeMouseFilter;
}
