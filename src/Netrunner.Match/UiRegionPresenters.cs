using System.Collections.Frozen;
using System.Collections.ObjectModel;
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
    string RunnerSummary);

/// <summary>Builds the immutable board-region projection used by the client.</summary>
public static class BoardRegionPresenter
{
    public static BoardRegionState Present(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new BoardRegionState(
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
}

/// <summary>Immutable action-control projection, including target semantics.</summary>
public sealed record ActionsRegionState(
    ActionsRegionStatus Status,
    DecisionViewModel? Decision,
    IReadOnlyList<LegalActionViewModel> Actions,
    IReadOnlySet<ActionId> TargetedActions);

/// <summary>
/// Reduces a Match snapshot to action controls. Action IDs and typed
/// parameters are retained; this presenter never creates or mutates actions.
/// </summary>
public static class ActionsRegionPresenter
{
    public static ActionsRegionState Present(MatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        DecisionViewModel? decision = snapshot.View.Decision;
        ActionsRegionStatus status = snapshot.IsOpponentThinking
            ? ActionsRegionStatus.OpponentThinking
            : decision is null || decision.OwnerSide != PlayerSide.Runner
                ? snapshot.RunOutcome is not null && snapshot.View.Run is null
                    ? ActionsRegionStatus.Complete
                    : ActionsRegionStatus.WaitingForDecision
                : ActionsRegionStatus.AwaitingRunnerDecision;

        LegalActionViewModel[] actions = snapshot.View.LegalActions.ToArray();
        return new ActionsRegionState(
            status,
            decision,
            new ReadOnlyCollection<LegalActionViewModel>(actions),
            actions
                .Where(IsTargetedAction)
                .Select(static action => action.Id)
                .ToFrozenSet());
    }

    public static bool IsTargetedAction(LegalActionViewModel action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return GetTargetCardId(action.Parameters) is not null
            || action.Parameters is BreachCandidateParameters;
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

    public static string? GetTargetCardId(ActionParameters parameters) => parameters switch
    {
        CardChoiceParameters value => value.CardId.Value,
        RunIceParameters value => value.IceId.Value,
        RunBreakerParameters value => value.BreakerId.Value,
        RunBreakParameters value => value.BreakerId.Value,
        RunCardPaymentParameters value => value.CardId.Value,
        BreachCandidateParameters { CardId: { } cardId } => cardId.Value,
        _ => null,
    };
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
            RunCreditsAdjustedEventPayload payload => $"Run pool {payload.Delta:+#;-#;0} · now {payload.RunCreditsAfter}.",
            RunEndedEventPayload payload => $"Run ended · {payload.Reason}.",
            _ => @event.Kind,
        };
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
        FocusRegionMode mode = snapshot.RunOutcome is not null && run is null
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
        return new FocusRegionState(mode, run, snapshot.RunOutcome);
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

        lines.Add($"Status · {(card.Rezzed ? "REZZED" : card.IsKnown ? "KNOWN" : "FACE-DOWN")}");
        if (card.Counters.Count > 0)
        {
            lines.Add(string.Join(" · ", card.Counters));
        }

        lines.Add(string.Empty);
        lines.Add(CardMarkupFormatter.Format(card.RulesText));
        return string.Join('\n', lines);
    }
}
