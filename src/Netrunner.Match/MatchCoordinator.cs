using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Channels;
using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Presentation-safe feedback from one coordinator operation.</summary>
public enum MatchFeedbackKind
{
    None,
    Rejected,
    AgentFailure,
    Cancelled,
}

public sealed record MatchFeedback(
    MatchFeedbackKind Kind,
    string Message,
    ApplyRejectionCode? RejectionCode = null)
{
    public bool IsError => Kind is MatchFeedbackKind.Rejected or MatchFeedbackKind.AgentFailure;
}

/// <summary>
/// Immutable publication from the match worker.  The observation and view are
/// Runner-filtered; they never contain the authoritative GameState.
/// </summary>
public sealed record MatchSnapshot(
    long Sequence,
    Observation Observation,
    MatchViewModel View,
    IReadOnlyList<EngineEvent> Events,
    MatchFeedback? Feedback,
    bool IsOpponentThinking,
    RunOutcomeViewModel? RunOutcome = null)
{
    public bool IsTerminal => View.Outcome.IsTerminal;

    public bool AwaitingHumanDecision => !IsOpponentThinking
        && View.Decision is { OwnerSide: PlayerSide.Runner };
}

/// <summary>Result of a human action submission, including rejection feedback.</summary>
public sealed record MatchSubmissionResult(
    bool Accepted,
    MatchSnapshot Snapshot,
    ApplyRejection? Rejection)
{
    public bool IsRejected => !Accepted;

    public string? Feedback => Rejection?.Message ?? Snapshot.Feedback?.Message;
}

/// <summary>Configuration for the asynchronous M3 coordinator.</summary>
public sealed class MatchCoordinatorOptions
{
    public TimeSpan AgentDelay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Injectable delay hook. Tests can use a ten-second virtual delay without
    /// making normal test runs sleep; production defaults to Task.Delay.
    /// </summary>
    public Func<TimeSpan, CancellationToken, ValueTask>? Delay { get; init; }

    public PlayerSide HumanSide { get; init; } = PlayerSide.Runner;
}

/// <summary>
/// Godot-independent owner of the authoritative M2 match.
///
/// One background loop serializes setup, agent decisions, and GameEngine.Apply
/// calls. The loop awaits agents and I/O without blocking a caller or a UI
/// thread. Human Runner actions are submitted as the issued decision id,
/// revision, and action id; every submission goes through GameEngine.Apply.
/// </summary>
public sealed class MatchCoordinator : IAsyncDisposable, IDisposable
{
    private readonly M2ScenarioDocument _document;
    private readonly IGameAgent _corpAgent;
    private readonly MatchCoordinatorOptions _options;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Channel<CoordinatorCommand> _commands =
        Channel.CreateUnbounded<CoordinatorCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly Task _worker;
    private readonly object _snapshotGate = new();
    private GameState? _state;
    private MatchSnapshot? _currentSnapshot;
    private RunOutcomeViewModel? _runOutcome;
    private long _publicationSequence;
    private bool _disposed;

    public MatchCoordinator(
        M2ScenarioDocument document,
        IGameAgent corpAgent,
        MatchCoordinatorOptions? options = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _corpAgent = corpAgent ?? throw new ArgumentNullException(nameof(corpAgent));
        _options = options ?? new MatchCoordinatorOptions();
        if (_options.HumanSide != PlayerSide.Runner)
        {
            throw new ArgumentException(
                "The M3 vertical slice supports a human Runner only.",
                nameof(options));
        }

        if (_options.AgentDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Agent delay must not be negative.");
        }

        _worker = RunWorkerAsync();
    }

    public MatchCoordinator(
        M2ScenarioDocument document,
        IGameAgent corpAgent,
        TimeSpan agentDelay,
        Func<TimeSpan, CancellationToken, ValueTask>? delay = null)
        : this(
            document,
            corpAgent,
            new MatchCoordinatorOptions
            {
                AgentDelay = agentDelay,
                Delay = delay,
            })
    {
    }

    /// <summary>Raised after a new immutable publication is available.</summary>
    public event Action<MatchSnapshot>? SnapshotPublished;

    public bool IsStarted => Volatile.Read(ref _state) is not null;

    public MatchSnapshot? CurrentSnapshot
    {
        get
        {
            lock (_snapshotGate)
            {
                return _currentSnapshot;
            }
        }
    }

    public Task<MatchSnapshot> StartAsync(CancellationToken cancellationToken = default)
    {
        return Enqueue(
            new StartCommand(),
            cancellationToken);
    }

    /// <summary>
    /// Submits the action exactly as issued.  The caller must use the current
    /// decision's revision and id; stale submissions are rejected without a
    /// state mutation and produce a new feedback-only publication.
    /// </summary>
    public Task<MatchSubmissionResult> SubmitActionAsync(
        ActionSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return Enqueue(
            new SubmitCommand(submission),
            cancellationToken);
    }

    public Task<MatchSubmissionResult> SubmitActionAsync(
        PlayerId actor,
        DecisionId decisionId,
        long revision,
        ActionId actionId,
        CancellationToken cancellationToken = default) =>
        SubmitActionAsync(
            new ActionSubmission(actor, decisionId, revision, actionId),
            cancellationToken);

    /// <summary>Submit an action from the current Runner decision.</summary>
    public Task<MatchSubmissionResult> SubmitRunnerActionAsync(
        ActionId actionId,
        CancellationToken cancellationToken = default)
    {
        MatchSnapshot? snapshot = CurrentSnapshot;
        ObservationDecision? decision = snapshot?.Observation.Decision;
        if (snapshot is null || decision is null)
        {
            throw new InvalidOperationException("The match is not currently awaiting a Runner decision.");
        }

        return SubmitActionAsync(
            new ActionSubmission(
                decision.Owner,
                decision.Id,
                decision.Revision,
                actionId),
            cancellationToken);
    }

    /// <summary>
    /// Returns a freshly filtered observation through the same Core boundary
    /// used by the coordinator. This is useful for diagnostics and does not
    /// expose the coordinator's authoritative state object.
    /// </summary>
    public Task<Observation> GetObservationAsync(
        PlayerSide viewer,
        CancellationToken cancellationToken = default) =>
        Enqueue(new ObservationCommand(viewer), cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        _commands.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            // Cancellation is the normal disposal path.
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

    private async Task RunWorkerAsync()
    {
        try
        {
            await foreach (CoordinatorCommand command in _commands.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
            {
                switch (command)
                {
                    case StartCommand start:
                        await HandleStartAsync(start).ConfigureAwait(false);
                        break;
                    case SubmitCommand submit:
                        await HandleSubmitAsync(submit).ConfigureAwait(false);
                        break;
                    case ObservationCommand observation:
                        HandleObservation(observation);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported match coordinator command '{command.GetType().Name}'.");
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            // Dispose/cancellation completes pending operations below.
        }
        catch (Exception exception)
        {
            while (_commands.Reader.TryRead(out CoordinatorCommand? command))
            {
                command.Fail(exception);
            }
        }
        finally
        {
            while (_commands.Reader.TryRead(out CoordinatorCommand? command))
            {
                command.Fail(new OperationCanceledException("The match coordinator stopped."));
            }
        }
    }

    private async Task HandleStartAsync(StartCommand command)
    {
        if (_state is not null)
        {
            command.Complete(CurrentSnapshot
                ?? throw new InvalidOperationException("The started match has no current publication."));
            return;
        }

        try
        {
            _document.Validate();
            M2ScenarioRunSpec run = _document.Run
                ?? throw new M2ScenarioException(["run: the scenario has no run contract."]);
            GameState initial = M2ScenarioSetup.Build(_document);
            ServerId server = ResolveServer(initial, run.Server);
            GameEngine.RunStartResult started = GameEngine.BeginRunWithEvents(
                initial,
                server,
                initiatingSource: new CardInstanceId(run.Source));
            _state = started.State;
            MatchSnapshot snapshot = Publish(started.Events, feedback: null, thinking: false);
            command.Complete(snapshot);

            await DriveAgentsAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ResolutionFailureException)
        {
            command.Fail(exception);
        }
    }

    private async Task HandleSubmitAsync(SubmitCommand command)
    {
        if (_state is null)
        {
            command.Fail(new InvalidOperationException("The match has not been started."));
            return;
        }

        ApplyResult applied = GameEngine.Apply(_state, command.Submission);
        if (!applied.Accepted)
        {
            MatchFeedback feedback = new(
                MatchFeedbackKind.Rejected,
                applied.Rejection?.Message ?? "The action was rejected.",
                applied.Rejection?.Code);
            MatchSnapshot rejected = Publish(Array.Empty<EngineEvent>(), feedback, thinking: false);
            command.Complete(new MatchSubmissionResult(false, rejected, applied.Rejection));
            return;
        }

        _state = applied.State;
        MatchSnapshot accepted = Publish(applied.Events, feedback: null, thinking: false);
        command.Complete(new MatchSubmissionResult(true, accepted, null));
        await DriveAgentsAsync().ConfigureAwait(false);
    }

    private void HandleObservation(ObservationCommand command)
    {
        if (_state is null)
        {
            command.Fail(new InvalidOperationException("The match has not been started."));
            return;
        }

        try
        {
            command.Complete(ObservationBuilder.Build(_state, command.Viewer));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            command.Fail(exception);
        }
    }

    private async Task DriveAgentsAsync()
    {
        while (_state is { } state
            && state.PendingDecision is { } pending
            && pending.Owner == state.Players.Single(player => player.Side == PlayerSide.Corp).Id)
        {
            Publish(Array.Empty<EngineEvent>(), feedback: null, thinking: true);
            // The published snapshot is intentionally Runner-filtered. Corp
            // policy receives its own filtered observation, never the
            // presentation snapshot and never GameState.
            Observation observation = ObservationBuilder.Build(state, PlayerSide.Corp);
            try
            {
                await DelayAsync(_options.AgentDelay, _cancellation.Token).ConfigureAwait(false);
                ActionId actionId = await _corpAgent
                    .ChooseActionAsync(observation, _cancellation.Token)
                    .ConfigureAwait(false);
                ApplyResult applied = GameEngine.Apply(
                    state,
                    new ActionSubmission(pending.Owner, pending.Id, pending.Revision, actionId));
                if (!applied.Accepted)
                {
                    MatchFeedback feedback = new(
                        MatchFeedbackKind.AgentFailure,
                        applied.Rejection?.Message ?? "The Corp agent action was rejected.",
                        applied.Rejection?.Code);
                    Publish(Array.Empty<EngineEvent>(), feedback, thinking: false);
                    return;
                }

                _state = applied.State;
                Publish(applied.Events, feedback: null, thinking: false);
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                Publish(
                    Array.Empty<EngineEvent>(),
                    new MatchFeedback(MatchFeedbackKind.Cancelled, "Opponent thinking was cancelled."),
                    thinking: false);
                return;
            }
            catch (Exception exception)
            {
                Publish(
                    Array.Empty<EngineEvent>(),
                    new MatchFeedback(MatchFeedbackKind.AgentFailure, exception.Message),
                    thinking: false);
                return;
            }
        }
    }

    private async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (_options.Delay is { } injected)
        {
            await injected(delay, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private MatchSnapshot Publish(
        IReadOnlyList<EngineEvent> events,
        MatchFeedback? feedback,
        bool thinking)
    {
        GameState state = _state
            ?? throw new InvalidOperationException("Cannot publish before the match starts.");
        foreach (EngineEvent @event in events)
        {
            if (@event.Payload is RunEndedEventPayload ended)
            {
                // Keep only a public, presentation-safe terminal summary. The
                // authoritative run is still owned and cleaned up by Core.
                _runOutcome = new RunOutcomeViewModel(
                    ended.RunId.Value,
                    ended.InitiatingSide,
                    ended.Reason,
                    ended.Successful);
            }
        }

        Observation observation = ObservationBuilder.Build(state, PlayerSide.Runner);
        MatchViewModel view = ObservationViewMapper.Build(observation);
        IReadOnlyList<EngineEvent> frozenEvents = new ReadOnlyCollection<EngineEvent>(events.ToArray());
        MatchSnapshot snapshot = new(
            Interlocked.Increment(ref _publicationSequence),
            observation,
            view,
            frozenEvents,
            feedback,
            thinking,
            _runOutcome);
        lock (_snapshotGate)
        {
            _currentSnapshot = snapshot;
        }

        SnapshotPublished?.Invoke(snapshot);
        return snapshot;
    }

    private static ServerId ResolveServer(GameState state, string reference)
    {
        if (!M2ScenarioDocument.TryParseServerRef(reference, state.Servers.Count - 3, out (ServerKind Kind, int RemoteIndex) parsed))
        {
            throw new ResolutionFailureException(
                $"scenario run server '{reference}' is not a valid server reference.");
        }

        ServerState? server = parsed.Kind == ServerKind.Remote
            ? state.Servers.ElementAt(3 + parsed.RemoteIndex)
            : state.Servers.SingleOrDefault(candidate => candidate.Kind == parsed.Kind);
        return server?.Id
            ?? throw new ResolutionFailureException(
                $"scenario run server '{reference}' was not found in the built state.");
    }

    private Task<T> Enqueue<T>(CoordinatorCommand<T> command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_commands.Writer.TryWrite(command))
        {
            throw new InvalidOperationException("The match coordinator is no longer accepting commands.");
        }

        return command.Task.WaitAsync(cancellationToken);
    }

    private abstract class CoordinatorCommand
    {
        public abstract void Fail(Exception exception);
    }

    private abstract class CoordinatorCommand<T> : CoordinatorCommand
    {
        protected readonly TaskCompletionSource<T> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Task => Completion.Task;

        public void Complete(T result) => Completion.TrySetResult(result);

        public override void Fail(Exception exception) => Completion.TrySetException(exception);
    }

    private sealed class StartCommand : CoordinatorCommand<MatchSnapshot>
    {
    }

    private sealed class SubmitCommand : CoordinatorCommand<MatchSubmissionResult>
    {
        public SubmitCommand(ActionSubmission submission) => Submission = submission;

        public ActionSubmission Submission { get; }
    }

    private sealed class ObservationCommand : CoordinatorCommand<Observation>
    {
        public ObservationCommand(PlayerSide viewer) => Viewer = viewer;

        public PlayerSide Viewer { get; }
    }
}

/// <summary>Loads the checked-in M2 document without relying on process cwd.</summary>
public static class M2ScenarioProvider
{
    private const string EmbeddedResourceName =
        "Netrunner.Match.Data.m2.representative-run.json";

    public static M2ScenarioDocument LoadRepresentative(string? path = null)
    {
        if (path is not null)
        {
            return Load(File.ReadAllText(path));
        }

        Assembly assembly = typeof(M2ScenarioProvider).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new FileNotFoundException(
                "The embedded M2 representative scenario is not available.",
                EmbeddedResourceName);
        using var reader = new StreamReader(stream);
        return Load(reader.ReadToEnd());
    }

    public static M2ScenarioDocument Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return EngineJson.Deserialize<M2ScenarioDocument>(json);
    }
}

/// <summary>Convenience factory for the playable M2 vertical slice.</summary>
public static class M2MatchFactory
{
    public static MatchCoordinator CreateRepresentative(
        MatchCoordinatorOptions? options = null,
        IGameAgent? corpAgent = null,
        string? scenarioPath = null)
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative(scenarioPath);
        M2ScenarioRunSpec run = document.Run
            ?? throw new M2ScenarioException(["run: the scenario has no run contract."]);
        RunPlayContract contract = new(
            run.Server,
            run.Source,
            run.Breaker ?? throw new M2ScenarioException(["run.breaker: a breaker is required."]),
            run.MaxPumps);
        IGameAgent agent = corpAgent
            ?? new RunContractAgent(contract);
        return new MatchCoordinator(document, agent, options);
    }

    /// <summary>
    /// Returns a Match-layer action policy for the checked-in representative
    /// Runner contract. The delegate consumes only a filtered observation and
    /// returns an already-issued ActionId; callers still submit through
    /// <see cref="MatchCoordinator.SubmitRunnerActionAsync"/>.
    /// </summary>
    public static Func<Observation, CancellationToken, ValueTask<ActionId>>
        CreateRepresentativeRunnerPolicy(string? scenarioPath = null)
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative(scenarioPath);
        M2ScenarioRunSpec run = document.Run
            ?? throw new M2ScenarioException(["run: the scenario has no run contract."]);
        var agent = new RunContractAgent(new RunPlayContract(
            run.Server,
            run.Source,
            run.Breaker ?? throw new M2ScenarioException(["run.breaker: a breaker is required."]),
            run.MaxPumps));
        return (observation, cancellationToken) => agent.ChooseActionAsync(observation, cancellationToken);
    }
}
