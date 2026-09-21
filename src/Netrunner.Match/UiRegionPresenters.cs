using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Globalization;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Immutable input for the board region. The Godot board owns controls and
/// layout, while this projection owns the values that may be rendered there.
/// Keeping the projection in Match makes board composition testable without a
/// running Godot scene and prevents a renderer from reaching Core state.
/// </summary>
public sealed record BoardRegionState(
    PlayerViewModel Corp,
    PlayerViewModel Runner,
    IReadOnlyList<ServerViewModel> Servers,
    ZoneViewModel RunnerHand,
    ZoneViewModel RunnerDeck,
    IReadOnlyList<CardViewModel> RunnerRig,
    IReadOnlyList<CardViewModel> CorpScore,
    IReadOnlyList<CardViewModel> RunnerScore,
    RunFocusViewModel? Run,
    string CorpSummary,
    string RunnerSummary)
{
    public PlayerSide Viewer { get; init; } = PlayerSide.Runner;
    public ZoneViewModel? CorpHand { get; init; }
    public ZoneViewModel? CorpDeck { get; init; }
    public HandSizeViewModel CorpHandSize { get; init; } =
        HandSizeViewModel.Unavailable(PlayerSide.Corp, 0);

    public HandSizeViewModel RunnerHandSize { get; init; } =
        HandSizeViewModel.Unavailable(PlayerSide.Runner, 0);

    public ScoreAreaViewModel CorpScoreArea { get; init; } =
        ScoreAreaViewModel.Empty(PlayerSide.Corp);

    public ScoreAreaViewModel RunnerScoreArea { get; init; } =
        ScoreAreaViewModel.Empty(PlayerSide.Runner);

    public IReadOnlyList<InstalledAdvancementViewModel> InstalledAdvancementCounters { get; init; } =
        Array.Empty<InstalledAdvancementViewModel>();

    public TagStateViewModel CorpTagState { get; init; } = new(PlayerSide.Corp, null);

    public TagStateViewModel RunnerTagState { get; init; } = new(PlayerSide.Runner, null);

    public IReadOnlyList<DamagePresentationViewModel> PendingDamage { get; init; } =
        Array.Empty<DamagePresentationViewModel>();

    public IReadOnlyList<ConditionalValueViewModel> ConditionalValues { get; init; } =
        Array.Empty<ConditionalValueViewModel>();

    public IdentityPanelState IdentityPanel { get; init; } = new(
        new IdentityPanelViewModel(PlayerSide.Corp, null),
        new IdentityPanelViewModel(PlayerSide.Runner, null));

    public ZahyaAccessCountViewModel ZahyaAccess { get; init; } = ZahyaAccessCountViewModel.Unknown;
}

/// <summary>
/// Immutable turn/priority panel projection. It contains only values already
/// present in the filtered Match view; missing or unknown enum values produce
/// an unavailable panel instead of a guessed phase or priority holder.
/// </summary>
public sealed record TurnRegionState(
    TurnViewModel Turn,
    DecisionViewModel? Decision,
    RunPriorityViewModel? Priority,
    bool IsForcedDiscard,
    bool IsAvailable,
    string Summary,
    string? Diagnostic)
{
    public static TurnRegionState Unavailable(string diagnostic) => new(
        TurnViewModel.Unavailable(diagnostic),
        null,
        null,
        false,
        false,
        "TURN / TIMING · unavailable",
        diagnostic);
}

/// <summary>Builds the UI-403 turn, timing, and priority panel.</summary>
public static class TurnRegionPresenter
{
    public static TurnRegionState Present(MatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        MatchViewModel view = snapshot.View;
        if (!view.Turn.IsAvailable
            || view.Turn.ActiveSide is not { } activeSide
            || view.Turn.Phase is not { } phase
            || !Enum.IsDefined(activeSide)
            || !Enum.IsDefined(phase)
            || view.Turn.TurnNumber is not { } turnNumber
            || turnNumber < 0)
        {
            return TurnRegionState.Unavailable(
                view.Turn.Diagnostic
                ?? "Turn/timing projection is unavailable: no valid public turn values were supplied.");
        }

        if (view.Turn.Step is { } step && !Enum.IsDefined(step))
        {
            return TurnRegionState.Unavailable(
                "Turn/timing projection is unavailable: the public timing step is unsupported.");
        }

        RunPriorityViewModel? priority = view.Run?.Priority;
        if (priority is not null
            && (!Enum.IsDefined(priority.Window)
                || !Enum.IsDefined(priority.Holder)
                || priority.ConsecutivePasses < 0))
        {
            return TurnRegionState.Unavailable(
                "Turn/timing projection is unavailable: the issued priority window is unsupported.");
        }

        DecisionViewModel? decision = view.Decision;
        bool knownDecision = decision is null
            || (Enum.IsDefined(decision.Kind) && Enum.IsDefined(decision.OwnerSide));
        if (!knownDecision)
        {
            return TurnRegionState.Unavailable(
                "Turn/timing projection is unavailable: the issued decision kind is unsupported.");
        }

        string summary =
            $"TURN {turnNumber} · ACTIVE {activeSide.ToString().ToUpperInvariant()} · PHASE {phase.ToString().ToUpperInvariant()}"
            + (view.Turn.Step is { } timingStep
                ? $" · TIMING {timingStep.ToString().ToUpperInvariant()}"
                : phase == TurnPhase.Setup ? " · MULLIGAN" : " · TIMING STEP UNAVAILABLE");
        if (priority is { } currentPriority)
        {
            summary +=
                $" · PRIORITY {currentPriority.Holder.ToString().ToUpperInvariant()} / {currentPriority.Window.ToString().ToUpperInvariant()}";
        }

        bool forcedDiscard = decision?.Kind == DecisionKind.TurnDiscard;
        if (decision is { } currentDecision)
        {
            summary += $" · DECISION {currentDecision.Kind.ToString().ToUpperInvariant()}";
        }

        return new TurnRegionState(
            view.Turn,
            decision,
            priority,
            forcedDiscard,
            true,
            summary,
            null);
    }
}

/// <summary>Builds the immutable board-region projection used by the client.</summary>
public static class BoardRegionPresenter
{
    public static BoardRegionState Present(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        BoardRegionState board = new(
            view.Corp,
            view.Runner,
            new ReadOnlyCollection<ServerViewModel>(view.Servers.ToArray()),
            view.RunnerHand,
            view.RunnerDeck,
            new ReadOnlyCollection<CardViewModel>(view.RunnerRig.ToArray()),
            new ReadOnlyCollection<CardViewModel>(view.CorpScore.ToArray()),
            new ReadOnlyCollection<CardViewModel>(view.RunnerScore.ToArray()),
            view.Run,
            FormatPlayer(view.Corp, view.CorpHand.Count, view.CorpDeck.Count, runPool: null),
            FormatPlayer(
                view.Runner,
                view.RunnerHand.Count,
                view.RunnerDeck.Count,
                view.Run is { RunCredits: > 0 } runWithPool ? runWithPool.RunCredits : null));
        return board with
        {
            Viewer = view.Viewer,
            CorpHand = view.CorpHand,
            CorpDeck = view.CorpDeck,
            CorpHandSize = view.CorpHandSize,
            RunnerHandSize = view.RunnerHandSize,
            CorpScoreArea = view.CorpScoreArea,
            RunnerScoreArea = view.RunnerScoreArea,
            InstalledAdvancementCounters = new ReadOnlyCollection<InstalledAdvancementViewModel>(
                view.InstalledAdvancementCounters.ToArray()),
            CorpSummary = board.CorpSummary + $"\n{view.CorpHandSize.Summary}\n{view.Corp.TagSummary}",
            RunnerSummary = board.RunnerSummary + $"\n{view.RunnerHandSize.Summary}\n{view.Runner.TagSummary}",
            CorpTagState = view.Corp.TagState,
            RunnerTagState = view.Runner.TagState,
            PendingDamage = new ReadOnlyCollection<DamagePresentationViewModel>(
                view.PendingDamage.ToArray()),
            ConditionalValues = new ReadOnlyCollection<ConditionalValueViewModel>(
                view.ConditionalValues.ToArray()),
            IdentityPanel = view.IdentityPanel,
            ZahyaAccess = Slice47PresentationProjector.PresentZahyaAccess(view),
        };
    }

    public static string FormatPlayer(
        PlayerViewModel player,
        int hand,
        int deck,
        int? runPool)
    {
        ArgumentNullException.ThrowIfNull(player);
        return $"Credits {player.Credits} · Clicks {player.Clicks} · Score {player.AgendaPoints}\n"
            + (runPool is { } pool ? $"Run pool {pool} · hosted, expires at run end\n" : string.Empty)
            + $"Hand {hand} · Deck {deck}";
    }
}

/// <summary>Status of the action controls for one immutable match snapshot.</summary>
public enum ActionsRegionStatus
{
    OpponentThinking,
    WaitingForDecision,
    AwaitingRunnerDecision,
    Complete,
    Unsupported,
}

/// <summary>One progressive-disclosure group for the active decision kind.</summary>
public sealed record ActionGroupViewModel(
    DecisionKind DecisionKind,
    string Label,
    IReadOnlyList<LegalActionViewModel> Actions);

/// <summary>Immutable action-control projection, including target semantics.</summary>
public sealed record ActionsRegionState(
    ActionsRegionStatus Status,
    DecisionViewModel? Decision,
    IReadOnlyList<LegalActionViewModel> Actions,
    IReadOnlySet<ActionId> TargetedActions)
{
    /// <summary>
    /// Actions retained for the active decision's supported progressive
    /// disclosure group. Unsupported/future decision kinds expose no controls.
    /// </summary>
    public IReadOnlyList<LegalActionViewModel> DisplayActions =>
        new ReadOnlyCollection<LegalActionViewModel>(
            ActionGroups.SelectMany(group => group.Actions).ToArray());

    /// <summary>Alias for clients that call the visible action set controls.</summary>
    public IReadOnlyList<LegalActionViewModel> VisibleActions => DisplayActions;

    public IReadOnlyList<ActionGroupViewModel> ActionGroups =>
        ActionsRegionPresenter.GroupActions(Decision, Actions);

    public bool IsForcedDiscard => Decision?.Kind == DecisionKind.TurnDiscard
        && Enum.IsDefined(Decision.Kind)
        && Enum.IsDefined(Decision.OwnerSide);

    public string? Diagnostic => Decision is { } decision
        && (!Enum.IsDefined(decision.Kind) || !Enum.IsDefined(decision.OwnerSide))
        ? $"No action controls rendered: unsupported issued decision '{decision.Kind}' for side '{decision.OwnerSide}'."
        : null;
}

/// <summary>
/// Reduces a Match snapshot to action controls. Action IDs and typed
/// parameters are retained; this presenter never creates or mutates actions.
/// </summary>
public static class ActionsRegionPresenter
{
    /// <summary>
    /// Adds only the cleanup explanation carried by the frozen install action.
    /// Candidate-only installs remain unchanged: Match must not guess whether
    /// an unselected candidate represents a Console or unique conflict.
    /// </summary>
    public static string Describe(
        ActionKind kind,
        string description,
        ActionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(parameters);
        string result = description;
        if (kind == ActionKind.InstallCard
            && parameters is InstallCardParameters install
            && (install.RequiredReplacementCardIds.Count > 0
                || install.SelectedReplacementCardIds.Count > 0)
            && !result.Contains("CONSOLE/UNIQUE CLEANUP", StringComparison.Ordinal))
        {
            // Keep the IDs visible so the control can explain exactly which
            // physical cards the engine's frozen install choice will clean up.
            // The reason is intentionally not inferred when both lists are empty.
            var details = new List<string>(2);
            if (install.RequiredReplacementCardIds.Count > 0)
            {
                details.Add(
                    $"REQUIRED [{FormatCardIds(install.RequiredReplacementCardIds)}]");
            }

            if (install.SelectedReplacementCardIds.Count > 0)
            {
                details.Add(
                    $"SELECTED [{FormatCardIds(install.SelectedReplacementCardIds)}]");
            }

            result = $"{result} · CONSOLE/UNIQUE CLEANUP · {string.Join(" · ", details)}";
        }

        CostQuote? quote = parameters switch
        {
            RezCardParameters rez => rez.CostQuote,
            RunIceParameters runIce => runIce.CostQuote,
            InstallCardParameters installParameters => installParameters.CostQuote,
            RunBreakParameters runBreak => runBreak.CostQuote,
            _ => null,
        };
        if (quote is not null)
        {
            string costSummary = CostQuotePresenter.Describe(kind, parameters, quote);
            if (!result.Contains(costSummary, StringComparison.Ordinal))
            {
                result = $"{result} · {costSummary}";
            }
        }

        return result;
    }

    public static LegalActionViewModel Describe(LegalActionViewModel action)
    {
        ArgumentNullException.ThrowIfNull(action);
        string description = Describe(action.Kind, action.Description, action.Parameters);
        return string.Equals(description, action.Description, StringComparison.Ordinal)
            ? action
            : action with { Description = description };
    }

    public static ActionsRegionState Present(MatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        DecisionViewModel? decision = snapshot.View.Decision;
        bool supportedDecision = decision is null
            || (Enum.IsDefined(decision.Kind) && Enum.IsDefined(decision.OwnerSide));
        ActionsRegionStatus status;
        if (snapshot.IsOpponentThinking)
        {
            status = ActionsRegionStatus.OpponentThinking;
        }
        else if (!supportedDecision)
        {
            status = ActionsRegionStatus.Unsupported;
        }
        else if (decision is null || decision.OwnerSide != snapshot.View.Viewer)
        {
            status = snapshot.IsTerminal || (snapshot.View.CorpIdentity is null && snapshot.RunOutcome is not null && snapshot.View.Run is null)
                ? ActionsRegionStatus.Complete
                : ActionsRegionStatus.WaitingForDecision;
        }
        else
        {
            status = ActionsRegionStatus.AwaitingRunnerDecision;
        }

        LegalActionViewModel[] actions = RunPaymentPreference.Apply(BoardIcebreakerPresenter.EncounterActions(snapshot.View, snapshot.View.LegalActions))
            .Select(Describe)
            .ToArray();
        IReadOnlyList<LegalActionViewModel> visibleActions = GroupActions(decision, actions)
            .SelectMany(group => group.Actions)
            .ToArray();
        return new ActionsRegionState(
            status,
            decision,
            new ReadOnlyCollection<LegalActionViewModel>(actions),
            visibleActions
                .Where(IsTargetedAction)
                .Select(static action => action.Id)
                .ToFrozenSet());
    }

    /// <summary>
    /// Groups only the action classes currently issued by the observation
    /// contract. This is progressive disclosure: a turn-action decision does
    /// not display run-window or discard controls that may belong to another
    /// decision, and a future/unknown decision fails closed.
    /// </summary>
    public static IReadOnlyList<ActionGroupViewModel> GroupActions(
        DecisionViewModel? decision,
        IReadOnlyList<LegalActionViewModel> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (decision is null
            || !Enum.IsDefined(decision.Kind)
            || !Enum.IsDefined(decision.OwnerSide))
        {
            return Array.Empty<ActionGroupViewModel>();
        }

        ActionKind[] allowed = decision.Kind switch
        {
            DecisionKind.TurnTiming =>
            [
                ActionKind.PassPriority,
                ActionKind.RezCard,
                ActionKind.ScoreAgenda,
                ActionKind.UseAbility,
            ],
            DecisionKind.TurnAction =>
            [
                ActionKind.GainCredit,
                ActionKind.DrawCard,
                ActionKind.PlayCard,
                ActionKind.InstallCard,
                ActionKind.StartRun,
                ActionKind.AdvanceCard,
                ActionKind.ChooseBehavior,
                ActionKind.RemoveTag,
                ActionKind.TrashTaggedResource,
                ActionKind.PurgeVirusCounters,
                ActionKind.UseAbility,
            ],
            DecisionKind.TurnDiscard => [ActionKind.ChooseCard],
            DecisionKind.BehaviorChoice => [ActionKind.ChooseBehavior],
            DecisionKind.TriggerChoice => [ActionKind.UseTrigger, ActionKind.DeclineTrigger],
            DecisionKind.IceSubroutineChoice => [ActionKind.ResolveIceSubroutine],
            DecisionKind.InstallChoice => [ActionKind.InstallCard],
            DecisionKind.MemoryCleanup => [ActionKind.TrashProgram],
            DecisionKind.TriggerOrdering => [ActionKind.OrderTriggers],
            DecisionKind.InterruptChoice =>
            [
                ActionKind.UsePrevention,
                ActionKind.DeclinePrevention,
                ActionKind.UseReplacement,
                ActionKind.DeclineReplacement,
            ],
            DecisionKind.ChooseCard => [ActionKind.ChooseCard],
            DecisionKind.ChooseNumber or DecisionKind.Setup => [ActionKind.ChooseNumber],
            DecisionKind.RunTiming =>
            [
                ActionKind.PassPriority,
                ActionKind.RezCard,
                ActionKind.RezIce,
                ActionKind.BreakSubroutines,
                ActionKind.PumpBreaker,
                ActionKind.UseBioroidAbility,
                ActionKind.UseAbility,
                ActionKind.JackOut,
                ActionKind.ContinueRun,
                ActionKind.ChooseBreachCandidate,
                ActionKind.TrashAccessedCard,
                ActionKind.NoAccessAction,
            ],
            _ => [],
        };

        if (allowed.Length == 0)
        {
            return Array.Empty<ActionGroupViewModel>();
        }

        HashSet<ActionKind> allowedSet = allowed.ToHashSet();
        LegalActionViewModel[] visible = actions
            .Where(action => Enum.IsDefined(action.Kind) && allowedSet.Contains(action.Kind))
            .ToArray();
        if (visible.Length == 0)
        {
            return Array.Empty<ActionGroupViewModel>();
        }

        string label = decision.Kind switch
        {
            DecisionKind.TurnTiming => "TURN TIMING / PRIORITY",
            DecisionKind.TurnAction => "BASIC ACTIONS ISSUED BY CORE",
            DecisionKind.TurnDiscard => "FORCED DISCARD",
            DecisionKind.BehaviorChoice => "BEHAVIOR CHOICE ISSUED BY CORE",
            DecisionKind.TriggerChoice => "TRIGGER CHOICE ISSUED BY CORE",
            DecisionKind.IceSubroutineChoice => "ICE SUBROUTINE CHOICE ISSUED BY CORE",
            DecisionKind.InstallChoice => "INSTALL CHOICE ISSUED BY CORE",
            DecisionKind.InterruptChoice => "INTERRUPT CHOICE ISSUED BY CORE",
            DecisionKind.RunTiming => "RUN WINDOW ACTIONS ISSUED BY CORE",
            _ => "ISSUED ACTIONS",
        };
        return
        [
            new ActionGroupViewModel(
                decision.Kind,
                label,
                new ReadOnlyCollection<LegalActionViewModel>(visible)),
        ];
    }

    public static bool IsTargetedAction(LegalActionViewModel action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return GetTargetCardId(action.Parameters) is not null
            || action.Parameters is BehaviorChoiceParameters { CardIds.Count: > 0 }
            || action.Parameters is AbilityActionParameters ability && (ability.TargetCardIds.Count > 0 || ability.AdditionalCostSelectionsOrEmpty.Count > 0)
            || action.Parameters is BreachCandidateParameters
            || Slice45PresentationProjector.GetTargetServerId(action.Parameters) is not null;
    }

    public static bool IsAccessAction(LegalActionViewModel action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action.Kind is
            ActionKind.StealAgenda
            or ActionKind.TrashAccessedCard
            or ActionKind.NoAccessAction
            or ActionKind.ChooseBreachCandidate;
    }

    public static string? GetTargetCardId(ActionParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return parameters is TrashTaggedResourceParameters tagged
            ? tagged.CardId.Value
            : Slice45PresentationProjector.GetTargetCardId(parameters);
    }

    /// <summary>
    /// Returns a viewer-safe typed server target for a run-event play. The
    /// action payload is the only source; card text and definition IDs are
    /// never consulted.
    /// </summary>
    public static string? GetTargetServerId(ActionParameters parameters) =>
        Slice45PresentationProjector.GetTargetServerId(parameters);

    private static string FormatCardIds(IReadOnlyList<CardInstanceId> cardIds) =>
        string.Join(", ", cardIds.Select(static cardId => cardId.Value));
}

/// <summary>
/// Retained event-feed state. Animation is still owned by Godot, but queue
/// trimming and ordering are deterministic and independently testable here.
/// </summary>
public sealed class EventFeedRegionPresenter
{
    public const int DefaultMaxVisibleLines = 8;

    private readonly Queue<string> _lines = new();

    public EventFeedRegionPresenter(int maxVisibleLines = DefaultMaxVisibleLines)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxVisibleLines, 1);
        MaxVisibleLines = maxVisibleLines;
    }

    public int MaxVisibleLines { get; }

    public IReadOnlyList<string> Lines => _lines.ToArray();

    public string RenderedText => string.Join('\n', _lines.Reverse());

    public void Append(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _lines.Enqueue(line);
        while (_lines.Count > MaxVisibleLines)
        {
            _lines.Dequeue();
        }
    }

    public void Clear() => _lines.Clear();

    /// <summary>Stable text-only description for the event-feed animation.</summary>
    public static string Describe(EngineEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return @event.Payload switch
        {
            RunInitiatedEventPayload payload => $"Run initiated against {payload.ServerId.Value}.",
            RunEventPlayedEventPayload payload => $"Played {payload.DefinitionId.Value}; hosted {payload.HostedCredits} run credits.",
            DamageDealtEventPayload payload => Slice46PresentationProjector.DescribeDamage(payload),
            TagsAdjustedEventPayload payload => Slice46PresentationProjector.DescribeTags(payload),
            IceApproachedEventPayload payload => $"Approached ICE {payload.IceId.Value}.",
            IceRezzedEventPayload payload => $"Corp rezzed ICE {payload.IceId.Value}.",
            EncounterInitializedEventPayload payload => $"Encountered {payload.IceId.Value} ({payload.SubroutineCount} subroutine(s)).",
            BreakerPumpedEventPayload payload => $"Pumped breaker {payload.BreakerId.Value}.",
            SubroutinesBrokenEventPayload payload => $"Broke {payload.BrokenCount} subroutine(s) with {payload.BreakerId.Value}.",
            RunSuccessfulEventPayload => "Run successful · breach boundary.",
            BreachStartedEventPayload payload => $"Breach started · {payload.CandidateCount} access candidate(s).",
            BreachCandidateChosenEventPayload payload => $"Access candidate chosen: {payload.CardId.Value}.",
            CardAccessedEventPayload payload => $"Accessed {payload.CardId.Value}.",
            AgendaStolenEventPayload payload => $"Agenda stolen · +{payload.AgendaPoints} points.",
            AccessedCardTrashedEventPayload payload => $"Trashed accessed card for {payload.TrashCost} credits.",
            HostedCreditPoolChangedEventPayload payload when Enum.IsDefined(payload.Operation)
                && Enum.IsDefined(payload.Restriction)
                => DescribeHostedCreditPool(payload),
            CardMovedEventPayload payload when payload.ToZoneKind is ZoneKind.CorpRemovedFromGame
                or ZoneKind.RunnerRemovedFromGame
                => $"Removed from game · {payload.CardId.Value} · {payload.FromZoneId.Value} → {payload.ToZoneId.Value}.",
            CardMovedEventPayload payload =>
                $"Card moved · {payload.CardId.Value} · {payload.FromZoneId.Value} → {payload.ToZoneId.Value}.",
            RunCreditsAdjustedEventPayload payload => $"Run pool {payload.Delta:+#;-#;0} · now {payload.RunCreditsAfter}.",
            RunEndedEventPayload payload => $"Run ended · {payload.Reason}.",
            IceSwappedEventPayload payload => Slice47PresentationProjector.DescribeIceSwap(payload),
            CardDerezzedEventPayload payload => Slice47PresentationProjector.DescribeDerez(payload),
            _ => @event.Kind,
        };
    }

    private static string DescribeHostedCreditPool(HostedCreditPoolChangedEventPayload payload)
    {
        string operation = payload.Operation.ToString().ToUpperInvariant();
        string delta = payload.Delta == 0
            ? "no change"
            : payload.Delta > 0
                ? $"+{payload.Delta}"
                : payload.Delta.ToString(CultureInfo.InvariantCulture);
        string emptied = payload.Emptied ? " · EMPTY" : string.Empty;
        string recipient = payload.Recipient is { } player
            ? $" · recipient {player.Value}"
            : string.Empty;
        return $"HOSTED CREDIT {operation} · {payload.CardId.Value} · {delta} · {payload.CountAfter} remaining{emptied}{recipient}.";
    }
}

/// <summary>Stable focus modes selected from one filtered Match snapshot.</summary>
public enum FocusRegionMode
{
    Empty,
    Outcome,
    Encounter,
    Access,
    PaidWindow,
    Run,
}

public sealed record FocusRegionState(
    FocusRegionMode Mode,
    RunFocusViewModel? Run,
    RunOutcomeViewModel? Outcome)
{
    public OutcomeViewModel? GameOutcome { get; init; }

    public EncounterViewModel? Encounter => Run?.Encounter;

    public AccessViewModel? Access => Run?.Access;

    public RunPriorityViewModel? Priority => Run?.Priority;
}

/// <summary>Selects the focus sub-region without constructing Godot controls.</summary>
public static class FocusRegionPresenter
{
    public static FocusRegionState Present(MatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RunFocusViewModel? run = snapshot.View.Run;
        if (run is not null && !HasKnownEnums(run))
        {
            return new FocusRegionState(FocusRegionMode.Empty, null, snapshot.RunOutcome);
        }

        bool representativeRunOutcome = snapshot.RunOutcome is not null
            && snapshot.View.CorpIdentity is null
            && !snapshot.View.Outcome.IsTerminal;
        FocusRegionMode mode = run is null
            && (representativeRunOutcome || snapshot.View.Outcome.IsTerminal)
            ? FocusRegionMode.Outcome
            : run is null
                ? FocusRegionMode.Empty
                : run.Encounter is not null
                    ? FocusRegionMode.Encounter
                    : run.Access.State != RunAccessState.NotStarted
                        ? FocusRegionMode.Access
                        : run.Priority is not null
                            ? FocusRegionMode.PaidWindow
                            : FocusRegionMode.Run;
        return new FocusRegionState(mode, run, snapshot.RunOutcome)
        {
            GameOutcome = snapshot.View.Outcome.IsTerminal ? snapshot.View.Outcome : null,
        };
    }

    private static bool HasKnownEnums(RunFocusViewModel run)
    {
        if (!Enum.IsDefined(run.Phase)
            || !Enum.IsDefined(run.Step)
            || !Enum.IsDefined(run.InitiatingSide)
            || !Enum.IsDefined(run.Access.State))
        {
            return false;
        }

        if (run.EndReason is { } endReason && !Enum.IsDefined(endReason))
        {
            return false;
        }

        if (run.Priority is { } priority
            && (!Enum.IsDefined(priority.Window)
                || !Enum.IsDefined(priority.Holder)
                || priority.ConsecutivePasses < 0))
        {
            return false;
        }

        return run.Encounter is null || Enum.IsDefined(run.Encounter.Status);
    }
}

/// <summary>Pure text projection for the inspected-card zoom region.</summary>
public static class CardZoomRegionPresenter
{
    public static string Format(CardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        List<string> lines =
        [
            $"{card.Type}{(card.Subtypes.Count == 0 ? string.Empty : " · " + string.Join(" · ", card.Subtypes))}",
        ];
        if (card.Cost is { } cost)
        {
            lines.Add($"Cost · {cost}");
        }

        if (card.Strength is { } strength)
        {
            lines.Add($"Strength · {strength}");
        }

        if (card.AgendaPoints is { } agendaPoints)
        {
            lines.Add($"Agenda points · {agendaPoints}");
        }

        if (card.TrashCost is { } trashCost)
        {
            lines.Add($"Trash cost · {trashCost}");
        }

        if (card.MemoryCost is { } memoryCost)
        {
            lines.Add($"Memory · {memoryCost}");
        }

        if (card.AdvancementRequirement is { } advancementRequirement)
        {
            lines.Add($"Advancement · {advancementRequirement}");
        }

        lines.Add($"Status · {card.StatusLabel}");
        if (card.HostLabel is { } host)
        {
            lines.Add(host);
        }
        if (card.FirstXUseCue is { } firstXUseCue)
        {
            lines.Add(firstXUseCue);
        }

        if (card.Counters.Count > 0)
        {
            lines.Add(string.Join(" · ", card.Counters));
        }

        lines.Add(string.Empty);
        lines.Add(CardMarkupFormatter.Format(card.RulesText));
        return string.Join('\n', lines);
    }
}
