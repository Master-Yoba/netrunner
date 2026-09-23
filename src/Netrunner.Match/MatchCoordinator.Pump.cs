using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class MatchCoordinator
{
    private bool _pumpQueued;
    private long _generation;
    private AgentEvaluationWork? _evaluation;

    /// <summary>Pause at a committed action boundary, invalidating any tentative bot result.</summary>
    public Task<MatchSnapshot> PauseAsync(CancellationToken cancellationToken = default) =>
        Enqueue(new PauseCommand(), cancellationToken);

    public Task<MatchSnapshot> ContinueAsync(CancellationToken cancellationToken = default) =>
        ContinueRecoveredSessionAsync(cancellationToken);

    /// <summary>Wait without occupying the worker until a client decision, pause, failure or outcome.</summary>
    public async Task<MatchSnapshot> WaitForHumanDecisionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Start or a submission may already be queued by a UI event. Flush
        // earlier commands before testing readiness; the old displayed decision
        // must not satisfy a wait intended for the result of that submission.
        await GetObservationAsync(_options.HumanSide, cancellationToken).ConfigureAwait(false);
        var completion = new TaskCompletionSource<MatchSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(MatchSnapshot snapshot)
        {
            if (snapshot.IsPaused || snapshot.IsTerminal || snapshot.Feedback?.IsError == true
                || snapshot.AwaitingHumanDecision && (!_options.AutoPassPriority
                    || snapshot.Observation.LegalActions.Count != 1
                    || snapshot.Observation.LegalActions[0].Kind != ActionKind.PassPriority))
                completion.TrySetResult(snapshot);
        }
        SnapshotPublished += Observe;
        try
        {
            if (CurrentSnapshot is { } current) Observe(current);
            Task winner = await Task.WhenAny(completion.Task, _worker).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (winner == _worker && !completion.Task.IsCompleted)
                throw new InvalidOperationException("The coordinator stopped before the next client boundary.");
            return await completion.Task.ConfigureAwait(false);
        }
        finally { SnapshotPublished -= Observe; }
    }

    /// <summary>Headless-client helper; ordinary observation reads remain immediate during thinking.</summary>
    public async Task<Observation> GetReadyObservationAsync(PlayerSide viewer, CancellationToken cancellationToken = default)
    {
        MatchSnapshot ready = await WaitForHumanDecisionAsync(cancellationToken).ConfigureAwait(false);
        return viewer == _options.HumanSide ? ready.Observation
            : await GetObservationAsync(viewer, cancellationToken).ConfigureAwait(false);
    }

    private void PauseAtBoundary()
    {
        if (_developerAdvance is not null) FinishDeveloperAdvance(DeveloperStopReason.Paused);
        _recoveredPaused = true;
        _generation++;
        _evaluation?.Cancellation.Cancel();
    }

    private void HandlePause(PauseCommand command)
    {
        if (_state is null)
        {
            command.Fail(new InvalidOperationException("The match has not been started."));
            return;
        }
        PauseAtBoundary();
        command.Complete(Publish([], null, thinking: false));
    }

    private Task DriveAgentsAsync()
    {
        if (!_pumpQueued && !_recoveredPaused && !_disposed)
        {
            _pumpQueued = _commands.Writer.TryWrite(new PumpCommand());
        }
        return Task.CompletedTask;
    }

    private void HandlePump()
    {
        if (TryStopDeveloperAdvance()) return;
        if (_recoveredPaused || _evaluation is not null || _cancellation.IsCancellationRequested
            || _state is not { PendingDecision: { } pending } state || state.Outcome.Kind != GameOutcomeKind.Ongoing)
            return;
        PlayerSide side = state.GetPlayer(pending.Owner).Side;
        IGameAgent agent = _corpAgent;
        if (side == _options.HumanSide && _developerAdvance is { } advance)
        {
            if (advance.Request.HumanAction is { } action)
            {
                CommitPumpAction(new(pending.Owner, pending.Id, pending.Revision, action));
                return;
            }
            agent = new FullGameContractAgent(side, _runtimeCardCatalog!);
        }
        else if (side == _options.HumanSide)
        {
            if (!_options.AutoPassPriority || pending.LegalActions.Count != 1
                || pending.LegalActions[0].Kind != ActionKind.PassPriority) return;
            CommitPumpAction(new(pending.Owner, pending.Id, pending.Revision, pending.LegalActions[0].Id));
            return;
        }

        var work = new AgentEvaluationWork(_generation, pending.Id, pending.Revision, pending.Owner, agent,
            (_corpAgent as OfflineAgent)?.Checkpoint,
            CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token, _developerAdvance?.Cancellation ?? default));
        _evaluation = work;
        Observation observation = ObservationBuilder.Build(state, side, _behaviorCatalog);
        Publish([], null, thinking: true);
        // Even synchronous policies run off the caller/render thread. Only this
        // evaluation touches tentative policy state; the worker commits it later.
        _ = Task.Run(async () =>
        {
            ActionId? action = null;
            Exception? failure = null;
            try
            {
                await DelayAsync(_developerAdvance is null ? _options.AgentDelay : TimeSpan.Zero, work.Cancellation.Token).ConfigureAwait(false);
                action = await work.Agent.ChooseActionAsync(observation, work.Cancellation.Token).ConfigureAwait(false);
                work.Cancellation.Token.ThrowIfCancellationRequested();
            }
            catch (Exception exception) { failure = exception; }
            if (!_commands.Writer.TryWrite(new AgentCompletedCommand(work, action, failure)))
                work.Cancellation.Dispose();
        });
    }

    private void HandleAgentCompleted(AgentCompletedCommand command)
    {
        AgentEvaluationWork work = command.Work;
        if (!ReferenceEquals(_evaluation, work)) return;
        _evaluation = null;
        bool cancelled = work.Cancellation.IsCancellationRequested;
        work.Cancellation.Dispose();
        bool current = !cancelled && !_recoveredPaused && work.Generation == _generation
            && _state?.PendingDecision is { } decision && decision.Id == work.DecisionId
            && decision.Revision == work.Revision && decision.Owner == work.Owner
            && ((_corpAgent as OfflineAgent)?.Checkpoint == work.Checkpoint);
        if (!current)
        {
            AgentLifecycleHost.Discard(work.Agent);
            if (TryStopDeveloperAdvance()) return;
            _ = DriveAgentsAsync();
            return;
        }
        if (command.Failure is not null || command.Action is null)
        {
            AgentLifecycleHost.Discard(_corpAgent);
            ReportPumpFailure(new(MatchFeedbackKind.AgentFailure,
                command.Failure?.Message ?? "Opponent returned no action."));
            return;
        }
        CommitPumpAction(new(work.Owner, work.DecisionId, work.Revision, command.Action.Value));
    }

    private void CommitPumpAction(ActionSubmission submission)
    {
        try
        {
            ApplyResult applied = ApplyEngineAction(_state!, submission);
            if (!applied.Accepted)
            {
                ReportPumpFailure(new(MatchFeedbackKind.AgentFailure,
                    applied.Rejection?.Message ?? "Automatic action was rejected.", applied.Rejection?.Code));
                return;
            }
            _state = applied.State;
            Publish(applied.Events, null, thinking: false);
            if (TryStopDeveloperAdvance()) return;
            _ = DriveAgentsAsync();
        }
        catch (Exception exception)
        {
            AgentLifecycleHost.Discard(_corpAgent);
            ReportPumpFailure(new(MatchFeedbackKind.AgentFailure, exception.Message));
            if (_traceFault is not null) throw;
        }
    }

    private void ReportPumpFailure(MatchFeedback feedback)
    {
        if (_developerAdvance is not null) FinishDeveloperAdvance(DeveloperStopReason.Failure, feedback);
        else Publish([], feedback, thinking: false);
    }

    private sealed record AgentEvaluationWork(long Generation, DecisionId DecisionId, long Revision, PlayerId Owner, IGameAgent Agent,
        OfflineAgentCheckpoint? Checkpoint, CancellationTokenSource Cancellation);
    private sealed class PumpCommand : CoordinatorCommand<bool>;
    private sealed class PauseCommand : CoordinatorCommand<MatchSnapshot>;
    private sealed class AgentCompletedCommand(AgentEvaluationWork work, ActionId? action, Exception? failure)
        : CoordinatorCommand<bool>
    {
        public AgentEvaluationWork Work { get; } = work;
        public ActionId? Action { get; } = action;
        public Exception? Failure { get; } = failure;
    }
}
