using Netrunner.Core;

namespace Netrunner.Match;

public enum DeveloperAdvanceTarget { ActionLimit, HumanDecision, Turn, Outcome }
public enum DeveloperStopReason { ActionLimit, HumanDecision, Turn, Outcome, NoDecision, Paused, Cancelled, Failure }
public sealed record DeveloperAdvanceRequest(DeveloperAdvanceTarget Target, int MaximumActions = 1000,
    int? TurnNumber = null, ActionId? HumanAction = null);
public sealed record DeveloperAdvanceResult(MatchSnapshot Snapshot, long AcceptedActions, DeveloperStopReason Reason);
public sealed record DeveloperCardView(string Id, string DefinitionId, string Title, string Zone, bool FaceUp);
public sealed record DeveloperInspection(long ActionSequence, string Origin, IReadOnlyList<DeveloperCardView> Cards);

public sealed partial class MatchCoordinator
{
    private readonly DeveloperSnapshotDocument? _developerRoot;
    private DeveloperAdvanceCommand? _developerAdvance;
    private long _developerAdvanceStart;

    public bool SupportsPlayerSaves => _fullMatch is not null;

    public MatchCoordinator(DeveloperSnapshotDocument document, MatchCoordinatorOptions options)
        : this(null, null, document.Opponent.Restore(
            options.BehaviorCatalog ?? throw new ArgumentException("Developer import requires a behavior catalog."),
            options.RuntimeCardCatalog ?? throw new ArgumentException("Developer import requires a runtime catalog.")),
            options, developerRoot: document)
    {
    }

    /// <summary>Explicit capability; presentation settings cannot enable these APIs.</summary>
    public Task<DeveloperSnapshotDocument> ExportDeveloperSnapshotAsync(CancellationToken cancellationToken = default) =>
        Enqueue(new DeveloperExportCommand(), cancellationToken);

    public Task<DeveloperInspection> InspectDeveloperStateAsync(CancellationToken cancellationToken = default) =>
        Enqueue(new DeveloperInspectCommand(), cancellationToken);

    public Task<DeveloperAdvanceResult> AdvanceDeveloperAsync(DeveloperAdvanceRequest request,
        CancellationToken cancellationToken = default) =>
        Enqueue(new DeveloperAdvanceCommand(request, cancellationToken), cancellationToken);

    private void RequireDeveloperState()
    {
        if (!_options.EnableDeveloperTools || _state is null || _runtimeCardCatalog is null || _replayInitial is null
            || _committedAgent is null || _traceFault is not null)
            throw new InvalidOperationException("Developer tools require an explicitly enabled, started engine session.");
    }

    private void HandleDeveloperExport(DeveloperExportCommand command)
    {
        try
        {
            RequireDeveloperState();
            PauseAtBoundary();
            var replay = new ReplayArtifact(_replayInitial!, _state!.InitialSeed, _replayActions,
                expectedFinalStateHash: DeterministicHash.ForState(_state));
            DeveloperSnapshotDocument document = DeveloperSnapshotDocument.Create(_options.HumanSide,
                _developerRoot?.Origin ?? DeveloperSnapshotDocument.SessionRoot,
                _state.CreateSnapshot(_behaviorCatalog, _runtimeCardCatalog!), replay, _committedAgent!);
            document.Validate(_behaviorCatalog, _runtimeCardCatalog!, verifyReplay: false);
            Publish([], null, false);
            command.Complete(document);
        }
        catch (Exception exception) { command.Fail(exception); }
    }

    private void HandleDeveloperInspect(DeveloperInspectCommand command)
    {
        try
        {
            RequireDeveloperState();
            // This private inspector is deliberately separate from all ordinary publications and agent observations.
            command.Complete(new(_state!.ActionSequence, _developerRoot?.Origin ?? DeveloperSnapshotDocument.SessionRoot,
                _state.Cards.Select(card => new DeveloperCardView(card.Id.Value, card.DefinitionId.Value,
                    _metadataResolver.TryResolve(card.DefinitionId.Value, out CardPresentationMetadata metadata)
                        ? metadata.Title : card.DefinitionId.Value,
                    card.ZoneId.Value, card.FaceUp)).ToArray()));
        }
        catch (Exception exception) { command.Fail(exception); }
    }

    private void HandleDeveloperAdvance(DeveloperAdvanceCommand command)
    {
        try
        {
            RequireDeveloperState();
            DeveloperAdvanceRequest request = command.Request;
            if (!Enum.IsDefined(request.Target) || request.MaximumActions is < 1 or > 10000
                || (request.Target == DeveloperAdvanceTarget.Turn) != (request.TurnNumber is not null)
                || request.TurnNumber < 1)
                throw new ArgumentException("Advance requires a limit of 1–10000 actions and a positive turn for a turn target.");
            if (_developerAdvance is not null) throw new InvalidOperationException("A developer advance is already running.");
            if (request.HumanAction is { } action && (request.MaximumActions != 1
                || _state!.PendingDecision is not { } pending || _state.GetPlayer(pending.Owner).Side != _options.HumanSide
                || !pending.LegalActions.Any(candidate => candidate.Id == action)))
                throw new ArgumentException("An override must be a currently issued human action for a single step.");
            command.Cancellation.ThrowIfCancellationRequested();
            PauseAtBoundary();
            _developerAdvance = command;
            _developerAdvanceStart = _state!.ActionSequence;
            _recoveredPaused = false;
            if (!TryStopDeveloperAdvance()) _ = DriveAgentsAsync();
        }
        catch (Exception exception) { command.Fail(exception); }
    }

    private bool TryStopDeveloperAdvance()
    {
        if (_developerAdvance is not { } advance) return false;
        DeveloperAdvanceRequest request = advance.Request;
        GameState state = _state!;
        DeveloperStopReason? reason = advance.Cancellation.IsCancellationRequested ? DeveloperStopReason.Cancelled
            : state.Outcome.Kind != GameOutcomeKind.Ongoing ? DeveloperStopReason.Outcome
            : state.ActionSequence - _developerAdvanceStart >= request.MaximumActions ? DeveloperStopReason.ActionLimit
            : state.PendingDecision is null ? DeveloperStopReason.NoDecision
            : request.Target == DeveloperAdvanceTarget.Turn && state.Turn.TurnNumber >= request.TurnNumber ? DeveloperStopReason.Turn
            : request.Target == DeveloperAdvanceTarget.HumanDecision && state.PendingDecision is { } pending
                && state.GetPlayer(pending.Owner).Side == _options.HumanSide ? DeveloperStopReason.HumanDecision
            : null;
        if (reason is null) return false;
        FinishDeveloperAdvance(reason.Value);
        return true;
    }

    private void FinishDeveloperAdvance(DeveloperStopReason reason, MatchFeedback? feedback = null)
    {
        if (_developerAdvance is not { } advance) return;
        _developerAdvance = null;
        PauseAtBoundary();
        MatchSnapshot snapshot = Publish([], feedback, false);
        advance.Complete(new(snapshot, _state!.ActionSequence - _developerAdvanceStart, reason));
    }

    private sealed class DeveloperExportCommand : CoordinatorCommand<DeveloperSnapshotDocument>;
    private sealed class DeveloperInspectCommand : CoordinatorCommand<DeveloperInspection>;
    private sealed class DeveloperAdvanceCommand(DeveloperAdvanceRequest request, CancellationToken cancellation)
        : CoordinatorCommand<DeveloperAdvanceResult>
    {
        public DeveloperAdvanceRequest Request { get; } = request;
        public CancellationToken Cancellation { get; } = cancellation;
    }
}
