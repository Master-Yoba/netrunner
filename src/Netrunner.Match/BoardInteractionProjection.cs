using System.Collections.ObjectModel;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// The semantic destination that a board surface can advertise while a hand
/// card is being dragged.  The Match layer owns this vocabulary; Godot only
/// maps each value to an editor-authored drop surface.
/// </summary>
public enum BoardInteractionDestinationKind
{
    GeneralPlay,
    RunnerRig,
    HostCard,
    ServerRoot,
    ServerIce,
    NewRemote,
    ServerTarget,
    RunnerHeap,
    CorpArchives,
}

/// <summary>
/// One board destination and the engine-issued actions that are legal for it.
/// <paramref name="TargetId"/> is a card ID for <see
/// cref="BoardInteractionDestinationKind.HostCard"/>, a server ID for server
/// destinations, and null for board-wide or new-remote destinations.
/// </summary>
public sealed record BoardInteractionDestination(
    BoardInteractionDestinationKind Kind,
    string? TargetId,
    IReadOnlyList<LegalActionViewModel> Actions);

/// <summary>One known card in the local hand and its legal drop surfaces.</summary>
public sealed record BoardInteractionSource(
    string CardId,
    IReadOnlyList<BoardInteractionDestination> Destinations);

/// <summary>
/// Viewer-safe card targets and option keys for an immediate engine choice.
/// Card IDs are filtered against the current view before this record crosses
/// the Match boundary, so a renderer cannot use an action payload to reveal a
/// hidden card.
/// </summary>
public sealed record BoardInteractionChoiceViewModel(
    LegalActionViewModel Action,
    IReadOnlyList<string> TargetCardIds,
    IReadOnlyList<string> OptionKeys);

/// <summary>Complete drag and immediate-choice projection for one view.</summary>
public sealed record BoardInteractionViewModel(
    IReadOnlyList<BoardInteractionSource> Sources,
    IReadOnlyList<BoardInteractionChoiceViewModel> Choices)
{
    public IReadOnlyList<BoardSpatialAction> SpatialActions { get; init; } = [];

    public static BoardInteractionViewModel Empty { get; } = new(
        Array.Empty<BoardInteractionSource>(),
        Array.Empty<BoardInteractionChoiceViewModel>());
}

/// <summary>
/// Projects engine-issued play, install, and hand-discard actions into semantic board drop
/// targets.  This projector never reads card subtypes, definitions, rules
/// text, or authoritative state; destination semantics come only from typed
/// action payloads already admitted to the viewer's observation.
/// </summary>
public static class BoardInteractionProjector
{
    /// <summary>
    /// Builds the board interaction projection for the active viewer. A
    /// non-local or missing decision deliberately produces an empty surface;
    /// clients must not offer drag submissions while another player owns the
    /// decision.
    /// </summary>
    public static BoardInteractionViewModel Project(MatchViewModel view) =>
        ProjectDrag(view) with { SpatialActions = BoardSpatialActions.Project(view) };

    private static BoardInteractionViewModel ProjectDrag(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!IsLocalDecision(view))
        {
            return BoardInteractionViewModel.Empty;
        }

        Dictionary<string, CardViewModel> knownHand = LocalKnownHand(view)
            .Where(static card => card.IsKnown && !string.IsNullOrWhiteSpace(card.Id))
            .GroupBy(static card => card.Id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        if (knownHand.Count == 0)
        {
            return new BoardInteractionViewModel(
                Array.Empty<BoardInteractionSource>(),
                ProjectChoices(view));
        }

        var builders = new List<SourceBuilder>();
        foreach (LegalActionViewModel action in view.LegalActions)
        {
            if (!TryGetSourceCardId(view, action, out string? cardId)
                || cardId is null
                || !knownHand.ContainsKey(cardId))
            {
                continue;
            }

            SourceBuilder? source = builders.FirstOrDefault(candidate =>
                string.Equals(candidate.CardId, cardId, StringComparison.Ordinal));
            if (source is null)
            {
                source = new SourceBuilder(cardId);
                builders.Add(source);
            }

            foreach (BoardInteractionDestination destination in DestinationsForAction(view, action))
            {
                source.Add(destination);
            }
        }

        BoardInteractionSource[] sources = builders
            .Select(static builder => builder.Build())
            .ToArray();
        return new BoardInteractionViewModel(
            new ReadOnlyCollection<BoardInteractionSource>(sources),
            ProjectChoices(view));
    }

    /// <summary>
    /// Projects every current legal action that carries an immediate card or
    /// option selection. The overload is useful to clients rendering a choice
    /// panel independently of hand drag surfaces.
    /// </summary>
    public static IReadOnlyList<BoardInteractionChoiceViewModel> TargetsForAction(
        MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!IsLocalDecision(view))
        {
            return Array.Empty<BoardInteractionChoiceViewModel>();
        }

        return new ReadOnlyCollection<BoardInteractionChoiceViewModel>(
            ProjectChoices(view).ToArray());
    }

    /// <summary>
    /// Projects one viewer-safe immediate follow-up choice. The supplied
    /// action must be the same action ID issued by the current view; callers
    /// cannot pass an altered payload to bypass the visibility filter.
    /// </summary>
    public static BoardInteractionChoiceViewModel? TargetsForAction(
        MatchViewModel view,
        LegalActionViewModel action)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(action);
        if (!IsLocalDecision(view))
        {
            return null;
        }

        LegalActionViewModel? issued = view.LegalActions.FirstOrDefault(candidate => candidate.Id == action.Id);
        if (issued is null)
        {
            return null;
        }

        HashSet<string> visibleCardIds = ExposedCardIds(view);
        return TryProjectChoice(issued, visibleCardIds, out BoardInteractionChoiceViewModel? choice)
            ? choice
            : null;
    }

    private static ReadOnlyCollection<BoardInteractionChoiceViewModel> ProjectChoices(MatchViewModel view)
    {
        HashSet<string> visibleCardIds = ExposedCardIds(view);
        var choices = new List<BoardInteractionChoiceViewModel>();
        foreach (LegalActionViewModel action in view.LegalActions)
        {
            if (TryProjectChoice(action, visibleCardIds, out BoardInteractionChoiceViewModel? choice)
                && choice is not null)
            {
                choices.Add(choice);
            }
        }

        return new ReadOnlyCollection<BoardInteractionChoiceViewModel>(choices.ToArray());
    }

    private static bool TryProjectChoice(
        LegalActionViewModel action,
        IReadOnlySet<string> visibleCardIds,
        out BoardInteractionChoiceViewModel? choice)
    {
        choice = null;
        List<string> targetCardIds = [];
        List<string> optionKeys = [];
        switch (action.Parameters)
        {
            case CardChoiceParameters cardChoice:
                AddVisibleCard(targetCardIds, cardChoice.CardId.Value, visibleCardIds);
                break;
            case BehaviorChoiceParameters behaviorChoice:
                foreach (CardInstanceId cardId in behaviorChoice.CardIds)
                {
                    AddVisibleCard(targetCardIds, cardId.Value, visibleCardIds);
                }

                AddDistinct(optionKeys, behaviorChoice.OptionKeys);
                break;
            case InstallCardParameters install:
                foreach (CardInstanceId cardId in install.CandidateReplacementCardIds
                    .Concat(install.RequiredReplacementCardIds)
                    .Concat(install.SelectedReplacementCardIds))
                {
                    AddVisibleCard(targetCardIds, cardId.Value, visibleCardIds);
                }

                break;
            default:
                return false;
        }

        if (targetCardIds.Count == 0 && optionKeys.Count == 0)
        {
            return false;
        }

        choice = new BoardInteractionChoiceViewModel(
            action,
            new ReadOnlyCollection<string>(targetCardIds.ToArray()),
            new ReadOnlyCollection<string>(optionKeys.ToArray()));
        return true;
    }

    private static IEnumerable<BoardInteractionDestination> DestinationsForAction(
        MatchViewModel view,
        LegalActionViewModel action)
    {
        switch (action.Parameters)
        {
            case CardChoiceParameters when view.Decision?.Kind == DecisionKind.TurnDiscard && action.Kind == ActionKind.ChooseCard:
                yield return Destination(view.Viewer == PlayerSide.Corp
                    ? BoardInteractionDestinationKind.CorpArchives : BoardInteractionDestinationKind.RunnerHeap, null, action);
                yield break;
            case PlayCardParameters play:
                // A targeted event remains playable from the board-wide event
                // surface, where the controller can stage a server choice,
                // and also advertises each already-materialized server target.
                yield return Destination(BoardInteractionDestinationKind.GeneralPlay, null, action);
                if (play.TargetServerId is { } serverId)
                {
                    yield return Destination(
                        BoardInteractionDestinationKind.ServerTarget,
                        StableValue(serverId.Value),
                        action);
                }

                yield break;
            case InstallCardParameters install:
                if (InstallDestination(view, install, action) is { } installDestination)
                {
                    yield return installDestination;
                }

                yield break;
            default:
                yield break;
        }
    }

    private static BoardInteractionDestination? InstallDestination(
        MatchViewModel view,
        InstallCardParameters install,
        LegalActionViewModel action)
    {
        if (install.HostCardId is { } hostCardId)
        {
            string hostId = StableValue(hostCardId.Value);
            return ExposedCardIds(view).Contains(hostId)
                ? Destination(BoardInteractionDestinationKind.HostCard, hostId, action)
                : null;
        }

        if (install.CreateNewRemote)
        {
            return Destination(BoardInteractionDestinationKind.NewRemote, null, action);
        }

        return install.DestinationKind switch
        {
            InstallDestinationKind.RunnerRig =>
                Destination(BoardInteractionDestinationKind.RunnerRig, null, action),
            InstallDestinationKind.ServerRoot when install.ServerId is { } serverId =>
                Destination(
                    BoardInteractionDestinationKind.ServerRoot,
                    StableValue(serverId.Value),
                    action),
            InstallDestinationKind.ServerIce when install.ServerId is { } serverId =>
                Destination(
                    BoardInteractionDestinationKind.ServerIce,
                    StableValue(serverId.Value),
                    action),
            _ => null,
        };
    }

    private static BoardInteractionDestination Destination(
        BoardInteractionDestinationKind kind,
        string? targetId,
        LegalActionViewModel action) => new(
        kind,
        targetId,
        new ReadOnlyCollection<LegalActionViewModel>([action]));

    private static bool TryGetSourceCardId(
        MatchViewModel view,
        LegalActionViewModel action,
        out string? cardId)
    {
        cardId = action.Parameters switch
        {
            PlayCardParameters play => play.CardId.Value,
            InstallCardParameters install => install.CardId.Value,
            CardChoiceParameters choice when view.Decision?.Kind == DecisionKind.TurnDiscard && action.Kind == ActionKind.ChooseCard => choice.CardId.Value,
            _ => null,
        };
        return (action.Kind is ActionKind.PlayCard or ActionKind.InstallCard
            || (view.Decision?.Kind == DecisionKind.TurnDiscard && action.Kind == ActionKind.ChooseCard))
            && !string.IsNullOrWhiteSpace(cardId);
    }

    /// <summary>
    /// TurnDiscard retains selected cards in hand until the complete set is chosen.
    /// Its issued candidates exclude those selections, so presentation can mark
    /// them as pending without mutating zones or reading the resolution stack.
    /// </summary>
    public static IReadOnlyList<CardViewModel> SelectedDiscards(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!IsLocalDecision(view) || view.Decision?.Kind != DecisionKind.TurnDiscard) return Array.Empty<CardViewModel>();
        HashSet<string> candidates = view.LegalActions
            .Where(action => action.Kind == ActionKind.ChooseCard)
            .Select(action => action.Parameters).OfType<CardChoiceParameters>()
            .Select(choice => choice.CardId.Value).ToHashSet(StringComparer.Ordinal);
        if (candidates.Count == 0) return Array.Empty<CardViewModel>();
        return LocalKnownHand(view).Where(card => card.IsKnown && !candidates.Contains(card.Id)).ToArray();
    }

    private static IReadOnlyList<CardViewModel> LocalKnownHand(MatchViewModel view) =>
        view.Viewer == PlayerSide.Corp
            ? view.CorpHand.VisibleCards
            : view.RunnerHand.VisibleCards;

    private static bool IsLocalDecision(MatchViewModel view) =>
        view.Decision is { } decision
        && decision.OwnerSide == view.Viewer
        && view.Outcome.Kind == GameOutcomeKind.Ongoing
        && view.Run is null
        && decision.Kind is DecisionKind.TurnAction or DecisionKind.InstallChoice or DecisionKind.ChooseCard
            or DecisionKind.BehaviorChoice or DecisionKind.MemoryCleanup or DecisionKind.TurnDiscard
        && Enum.IsDefined(decision.Kind)
        && Enum.IsDefined(decision.OwnerSide);

    /// <summary>
    /// Returns physical IDs that the observation has already exposed to the
    /// viewer. A card may be face-down or lack resolver metadata while its
    /// positional ID is still intentionally exposed as a legal host target.
    /// </summary>
    private static HashSet<string> ExposedCardIds(MatchViewModel view)
    {
        var cards = new List<CardViewModel>();
        AddCards(cards, view.CorpHand.VisibleCards);
        AddCards(cards, view.RunnerHand.VisibleCards);
        AddCards(cards, view.CorpDiscard);
        AddCards(cards, view.RunnerHeap);
        AddCards(cards, view.RunnerRig);
        AddCards(cards, view.CorpScore);
        AddCards(cards, view.RunnerScore);
        AddCard(cards, view.CorpIdentity);
        AddCard(cards, view.RunnerIdentity);
        foreach (ServerViewModel server in view.Servers)
        {
            AddCards(cards, server.Ice);
            AddCards(cards, server.Root);
        }

        if (view.Run is { } run)
        {
            AddCard(cards, run.CurrentIce);
            AddCard(cards, run.InitiatingSource);
            AddCard(cards, run.Encounter?.Ice);
            AddCards(cards, run.Access.Candidates);
            AddCards(cards, run.Access.Chosen);
            AddCards(cards, run.Access.Accessed);
        }

        HashSet<string> exposedIds = cards
            .Where(static card => !string.IsNullOrWhiteSpace(card.Id))
            .Select(static card => card.Id)
            .ToHashSet(StringComparer.Ordinal);

        // Public ICE positions intentionally survive card redaction. A
        // position can therefore be a legal host target even when the
        // corresponding server.Ice slot is null and no CardViewModel exists.
        // Keep the physical ID, but do not synthesize any card metadata from
        // it.
        foreach (ServerViewModel server in view.Servers)
        {
            foreach (IcePositionViewModel position in server.IcePositions)
            {
                if (!string.IsNullOrWhiteSpace(position.CardId))
                {
                    exposedIds.Add(position.CardId);
                }
            }
        }

        return exposedIds;
    }

    private static void AddCards(List<CardViewModel> destination, IEnumerable<CardViewModel?> cards)
    {
        foreach (CardViewModel? card in cards)
        {
            AddCard(destination, card);
        }
    }

    private static void AddCard(List<CardViewModel> destination, CardViewModel? card)
    {
        if (card is not null)
        {
            destination.Add(card);
        }
    }

    private static void AddVisibleCard(
        List<string> destination,
        string cardId,
        IReadOnlySet<string> visibleCardIds)
    {
        string stableId = StableValue(cardId);
        if (visibleCardIds.Contains(stableId) && !destination.Contains(stableId, StringComparer.Ordinal))
        {
            destination.Add(stableId);
        }
    }

    private static void AddDistinct(List<string> destination, IEnumerable<string> values)
    {
        foreach (string value in values)
        {
            string stableValue = StableValue(value);
            if (!string.IsNullOrWhiteSpace(stableValue)
                && !destination.Contains(stableValue, StringComparer.Ordinal))
            {
                destination.Add(stableValue);
            }
        }
    }

    private static string StableValue(string value) => value ?? string.Empty;

    private sealed class SourceBuilder
    {
        private readonly List<DestinationBuilder> _destinations = [];

        public SourceBuilder(string cardId) => CardId = cardId;

        public string CardId { get; }

        public void Add(BoardInteractionDestination destination)
        {
            DestinationBuilder? builder = _destinations.FirstOrDefault(candidate =>
                candidate.Kind == destination.Kind
                && string.Equals(candidate.TargetId, destination.TargetId, StringComparison.Ordinal));
            if (builder is null)
            {
                builder = new DestinationBuilder(destination.Kind, destination.TargetId);
                _destinations.Add(builder);
            }

            builder.Actions.AddRange(destination.Actions);
        }

        public BoardInteractionSource Build() => new(
            CardId,
            new ReadOnlyCollection<BoardInteractionDestination>(
                _destinations.Select(static destination => destination.Build()).ToArray()));
    }

    private sealed class DestinationBuilder
    {
        public DestinationBuilder(BoardInteractionDestinationKind kind, string? targetId)
        {
            Kind = kind;
            TargetId = targetId;
        }

        public BoardInteractionDestinationKind Kind { get; }

        public string? TargetId { get; }

        public List<LegalActionViewModel> Actions { get; } = [];

        public BoardInteractionDestination Build() => new(
            Kind,
            TargetId,
            new ReadOnlyCollection<LegalActionViewModel>(Actions.ToArray()));
    }
}
