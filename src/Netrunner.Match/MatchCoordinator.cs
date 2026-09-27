using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Channels;
using Netrunner.AI;
using Netrunner.Cards;
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
    /// <summary>
    /// The side that owns a Core setup decision, when one is pending. This is
    /// public setup telemetry only: the viewer still receives no opponent
    /// decision ID, revision, prompt, or legal actions.
    /// </summary>
    public PlayerSide? SetupDecisionOwnerSide { get; init; }

    /// <summary>Accepted engine actions, excluding UI and opponent-thinking publications.</summary>
    public long ActionCount { get; init; }

    public IReadOnlyList<MatchHistoryEntry> History { get; init; } = Array.Empty<MatchHistoryEntry>();

    /// <summary>Viewer-safe board changes made by the accepted action, in order (engine plan E4b).</summary>
    public IReadOnlyList<PresentationEvent> PresentationEvents { get; init; } = Array.Empty<PresentationEvent>();

    /// <summary>The player's next turn action if they pass the own-turn window they hold (invisible windows).</summary>
    public TurnPreview? TurnPreview { get; init; }

    public bool IsTerminal => View.Outcome.IsTerminal;

    public bool IsPaused { get; init; }

    public bool AwaitingHumanDecision => !IsPaused && !IsOpponentThinking
        && View.Decision?.OwnerSide == View.Viewer;
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
    public bool EnableDeveloperTools { get; init; }

    public TimeSpan AgentDelay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Injectable delay hook. Tests can use a ten-second virtual delay without
    /// making normal test runs sleep; production defaults to Task.Delay.
    /// </summary>
    public Func<TimeSpan, CancellationToken, ValueTask>? Delay { get; init; }

    public PlayerSide HumanSide { get; init; } = PlayerSide.Runner;

    /// <summary>
    /// Automatically submit a human PassPriority when it is the only legal
    /// action in the issued decision. The default keeps the caller-driven
    /// behavior used by existing clients.
    /// </summary>
    public bool AutoPassPriority { get; init; }

    /// <summary>
    /// Immutable card metadata supplied by composition. When omitted, the
    /// generated Cards resource is used; Match never reads the ignored source
    /// directory itself.
    /// </summary>
    public ICardMetadataResolver? CardMetadataResolver { get; init; }

    /// <summary>Short alias retained for integrations that call this a metadata resolver.</summary>
    public ICardMetadataResolver? MetadataResolver { get; init; }

    /// <summary>
    /// Immutable compiled behavior catalog supplied at match composition.
    /// When omitted, the Gateway catalog is built once for this coordinator;
    /// no process-wide mutable registry is used by the worker.
    /// </summary>
    public IBehaviorCatalog? BehaviorCatalog { get; init; }

    /// <summary>
    /// Immutable printed-card runtime metadata supplied at match composition.
    /// When present, every setup, run, and action reduction uses this same
    /// snapshot. Omitting it preserves the legacy behavior-only adapter.
    /// </summary>
    public IRuntimeCardCatalog? RuntimeCardCatalog { get; init; }

    /// <summary>
    /// Optional directory for a new durable session trace. The directory may
    /// contain unrelated UI files, but authoritative trace files are never
    /// overwritten.
    /// </summary>
    public string? SessionTraceDirectory { get; init; }

    /// <summary>
    /// Existing durable trace to recover. A resumed coordinator publishes the
    /// recovered decision paused until <see cref="MatchCoordinator.ContinueRecoveredSessionAsync"/>
    /// is called, so an opponent agent never advances before the UI confirms.
    /// </summary>
    public string? ResumeTraceDirectory { get; init; }

    /// <summary>Optional earlier accepted-action boundary within the source trace.</summary>
    public long? ResumeThroughActionSequence { get; init; }
}

/// <summary>
/// Godot-independent owner of the authoritative M2 match.
///
/// One background loop serializes setup, agent decisions, and GameEngine.Apply
/// calls. The loop awaits agents and I/O without blocking a caller or a UI
/// thread. Human Runner actions are submitted as the issued decision id,
/// revision, and action id; every submission goes through GameEngine.Apply.
/// </summary>
public sealed partial class MatchCoordinator : IAsyncDisposable, IDisposable
{
    private readonly M2ScenarioDocument? _document;
    private readonly FullMatchDefinition? _fullMatch;
    private IGameAgent _corpAgent;
    private readonly MatchCoordinatorOptions _options;
    private readonly ICardMetadataResolver _metadataResolver;
    private readonly IBehaviorCatalog _suppliedBehaviorCatalog;
    private readonly IRuntimeCardCatalog? _runtimeCardCatalog;
    private IBehaviorCatalog _behaviorCatalog;
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
    private readonly MatchHistoryProjector _historyProjector = new();
    private (MatchViewModel Before, PlayerSide Actor, LegalAction Action)? _historyContext;
    private readonly CardKeyTracker _cardKeys = new();
    private IReadOnlyDictionary<CardInstanceId, string> _currentCardKeys = new Dictionary<CardInstanceId, string>();
    private GameState? _presentationBefore;
    private SessionTrace? _sessionTrace;
    private Exception? _traceFault;
    private bool _recoveredPaused;
    private bool _disposed;

    public MatchCoordinator(
        M2ScenarioDocument document,
        IGameAgent corpAgent,
        MatchCoordinatorOptions? options = null)
        : this(document ?? throw new ArgumentNullException(nameof(document)), null, corpAgent, options)
    {
    }

    public MatchCoordinator(FullMatchDefinition definition, IGameAgent opponentAgent, MatchCoordinatorOptions? options = null)
        : this(null, definition ?? throw new ArgumentNullException(nameof(definition)), opponentAgent, options)
    {
    }

    private MatchCoordinator(M2ScenarioDocument? document, FullMatchDefinition? definition, IGameAgent corpAgent,
        MatchCoordinatorOptions? options, SoloSaveDocument? save = null, DeveloperSnapshotDocument? developerRoot = null)
    {
        _developerRoot = developerRoot;
        _resumeSave = save;
        _document = document;
        _fullMatch = definition;
        _corpAgent = corpAgent ?? throw new ArgumentNullException(nameof(corpAgent));
        _options = options ?? new MatchCoordinatorOptions();
        AgentLifecycleHost.ValidateSeat(_corpAgent, _options.HumanSide == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp);
        if (_corpAgent is IAgentLifecycle && !string.IsNullOrWhiteSpace(_options.ResumeTraceDirectory)
            && (!File.Exists(Path.Combine(_options.ResumeTraceDirectory, SessionTrace.ManifestFileName))
                || SessionTrace.ReadManifest(_options.ResumeTraceDirectory).SchemaVersion != SessionTrace.PolicySchemaVersion))
            throw new ArgumentException("Legacy traces do not contain policy checkpoints; stateful-agent recovery requires the M5 save format.", nameof(options));
        _metadataResolver = definition?.Metadata ?? _options.CardMetadataResolver
            ?? _options.MetadataResolver
            ?? CardMetadataResolvers.Default;
        _suppliedBehaviorCatalog = definition?.Behavior ?? _options.BehaviorCatalog
            ?? GatewayBehaviorCatalog.Create();
        _runtimeCardCatalog = definition?.Runtime ?? _options.RuntimeCardCatalog;
        // Keep the field initialized until StartAsync validates the document;
        // the worker replaces it with the immutable scenario slice before any
        // engine or presentation operation runs.
        _behaviorCatalog = _suppliedBehaviorCatalog;
        if (!Enum.IsDefined(_options.HumanSide) || definition is null && developerRoot is null && _options.HumanSide != PlayerSide.Runner)
        {
            throw new ArgumentException(
                "The M3 vertical slice supports a human Runner only.",
                nameof(options));
        }

        if (_options.AgentDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Agent delay must not be negative.");
        }

        if (_options.ResumeThroughActionSequence is { } resumeSequence
            && (resumeSequence < 0 || string.IsNullOrWhiteSpace(_options.ResumeTraceDirectory)))
            throw new ArgumentException("A nonnegative resume action sequence requires ResumeTraceDirectory.", nameof(options));

        if (!string.IsNullOrWhiteSpace(_options.ResumeTraceDirectory)
            && string.IsNullOrWhiteSpace(_options.SessionTraceDirectory))
        {
            throw new ArgumentException(
                "SessionTraceDirectory must name a new destination when ResumeTraceDirectory is configured.",
                nameof(options));
        }

        if (!string.IsNullOrWhiteSpace(_options.ResumeTraceDirectory)
            && string.Equals(
                Path.GetFullPath(_options.SessionTraceDirectory!),
                Path.GetFullPath(_options.ResumeTraceDirectory),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "SessionTraceDirectory and ResumeTraceDirectory must be distinct.",
                nameof(options));
        }

        if (save is not null && (_options.HumanSide != save.Metadata.HumanSide || _options.ResumeTraceDirectory is not null))
            throw new ArgumentException("Saved human side or recovery options do not match the requested session.", nameof(options));
        if (developerRoot is not null && (!_options.EnableDeveloperTools || _options.HumanSide != developerRoot.HumanSide
            || _options.ResumeTraceDirectory is not null))
            throw new ArgumentException("Developer import requires an explicit capability and the saved human seat.", nameof(options));
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
    /// Allows a recovered session to resume its normal agent-driving loop.
    /// Resume startup deliberately publishes the exact recovered decision
    /// first, with no agent call, until this command is accepted.
    /// </summary>
    public Task<MatchSnapshot> ContinueRecoveredSessionAsync(CancellationToken cancellationToken = default)
    {
        return Enqueue(
            new ContinueRecoveredCommand(),
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
        => SubmitHumanActionAsync(actionId, cancellationToken);

    /// <summary>Submit an issued choice for the configured human side.</summary>
    public Task<MatchSubmissionResult> SubmitHumanActionAsync(ActionId actionId, CancellationToken cancellationToken = default)
    {
        MatchSnapshot? snapshot = CurrentSnapshot;
        ObservationDecision? decision = snapshot?.Observation.Decision;
        if (snapshot is null || decision is null)
        {
            throw new InvalidOperationException("The match is not currently awaiting a human decision.");
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
        CoordinatorCommand? activeCommand = null;
        try
        {
            await foreach (CoordinatorCommand command in _commands.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
            {
                activeCommand = command;
                switch (command)
                {
                    case StartCommand start:
                        await HandleStartAsync(start).ConfigureAwait(false);
                        break;
                    case ContinueRecoveredCommand continueRecovered:
                        await HandleContinueRecoveredAsync(continueRecovered).ConfigureAwait(false);
                        break;
                    case SubmitCommand submit:
                        await HandleSubmitAsync(submit).ConfigureAwait(false);
                        break;
                    case PumpCommand:
                        _pumpQueued = false;
                        HandlePump();
                        break;
                    case AgentCompletedCommand completed:
                        HandleAgentCompleted(completed);
                        break;
                    case CaptureSaveCommand capture:
                        HandleCaptureSave(capture);
                        break;
                    case PauseCommand pause:
                        HandlePause(pause);
                        break;
                    case ObservationCommand observation:
                        HandleObservation(observation);
                        break;
                    case DeveloperExportCommand export:
                        HandleDeveloperExport(export);
                        break;
                    case DeveloperInspectCommand inspect:
                        HandleDeveloperInspect(inspect);
                        break;
                    case DeveloperAdvanceCommand advance:
                        HandleDeveloperAdvance(advance);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported match coordinator command '{command.GetType().Name}'.");
                }
                activeCommand = null;
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            // Dispose/cancellation completes pending operations below.
        }
        catch (Exception exception)
        {
            _commands.Writer.TryComplete(exception);
            activeCommand?.Fail(exception);
            while (_commands.Reader.TryRead(out CoordinatorCommand? command))
            {
                command.Fail(exception);
            }
        }
        finally
        {
            _developerAdvance?.Fail(new OperationCanceledException("The match coordinator stopped."));
            try
            {
                _sessionTrace?.Dispose();
            }
            catch (Exception exception)
            {
                _traceFault ??= exception;
            }

            _commands.Writer.TryComplete();
            activeCommand?.Fail(new OperationCanceledException("The match coordinator stopped."));
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
            if (_developerRoot is not null)
            {
                _state = _developerRoot.Validate(_behaviorCatalog, _runtimeCardCatalog!);
                InitializePersistence(_state, _developerRoot.Replay);
                RestoreHistory(_developerRoot.Replay);
                InitializeSessionTrace(_state);
                _recoveredPaused = true;
                command.Complete(Publish([], null, false));
                return;
            }
            if (_resumeSave is not null)
            {
                RestoreSave();
                command.Complete(Publish([], null, thinking: false));
                return;
            }
            if (!string.IsNullOrWhiteSpace(_options.ResumeTraceDirectory))
            {
                SessionRecoveryResult recovered = RecoverSession(_options.ResumeTraceDirectory);
                _state = recovered.State;
                if (recovered.Policy is not null)
                    _corpAgent = recovered.Policy.Restore(_behaviorCatalog, _runtimeCardCatalog!);
                InitializePersistence(recovered.State, recovered.SessionReplay);
                if (_fullMatch is not null) RestoreHistory(recovered.SessionReplay);
                InitializeSessionTrace(recovered.State);
                _recoveredPaused = true;
                command.Complete(Publish(Array.Empty<EngineEvent>(), feedback: null, thinking: false));
                return;
            }

            if (_fullMatch is not null)
            {
                GameEngine.TurnStartResult setup = _fullMatch.Start();
                _state = setup.State;
                AgentLifecycleHost.Observe(_corpAgent, _state, _behaviorCatalog);
                InitializePersistence(setup.State);
                InitializeSessionTrace(setup.State);
                command.Complete(Publish(setup.Events, feedback: null, thinking: false));
                await DriveAgentsAsync().ConfigureAwait(false);
                return;
            }

            BehaviorCatalog scenarioCatalog = _document!.Validate(_suppliedBehaviorCatalog);
            _behaviorCatalog = scenarioCatalog;
            M2ScenarioRunSpec run = _document.Run
                ?? throw new M2ScenarioException(["run: the scenario has no run contract."]);
            GameState initial = M2ScenarioSetup.Build(
                _document,
                scenarioCatalog,
                _runtimeCardCatalog);
            ServerId server = ResolveServer(initial, run.Server);
            GameEngine.RunStartResult started = _runtimeCardCatalog is null
                ? GameEngine.BeginRunWithEvents(
                    initial,
                    server,
                    _behaviorCatalog,
                    initiatingSource: new CardInstanceId(run.Source))
                : GameEngine.BeginRunWithEvents(
                    initial,
                    server,
                    _behaviorCatalog,
                    _runtimeCardCatalog,
                    initiatingSource: new CardInstanceId(run.Source));
            _state = started.State;
            AgentLifecycleHost.Observe(_corpAgent, _state, _behaviorCatalog);
            InitializePersistence(started.State);
            InitializeSessionTrace(started.State);
            MatchSnapshot snapshot = Publish(started.Events, feedback: null, thinking: false);
            command.Complete(snapshot);

            await DriveAgentsAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ResolutionFailureException)
        {
            command.Fail(exception);
        }
    }

    private async Task HandleContinueRecoveredAsync(ContinueRecoveredCommand command)
    {
        if (_state is null)
        {
            command.Fail(new InvalidOperationException("The match has not been started."));
            return;
        }

        if (!_recoveredPaused)
        {
            command.Complete(CurrentSnapshot
                ?? throw new InvalidOperationException("The started match has no current publication."));
            return;
        }

        _recoveredPaused = false;
        MatchSnapshot snapshot = Publish(Array.Empty<EngineEvent>(), feedback: null, thinking: false);
        command.Complete(snapshot);
        // Complete the UI command at the paused boundary. Agent driving may
        // immediately publish a later thinking/decision snapshot.
        await DriveAgentsAsync().ConfigureAwait(false);
    }

    private SessionRecoveryResult RecoverSession(string directory)
    {
        if (_fullMatch is null)
        {
            _behaviorCatalog = _document!.Validate(_suppliedBehaviorCatalog);
        }

        SessionRecoveryResult recovered = SessionTrace.Recover(
            directory,
            _behaviorCatalog,
            _runtimeCardCatalog,
            _options.ResumeThroughActionSequence);
        if (recovered.Manifest.HumanSide != _options.HumanSide)
        {
            throw new SessionTraceException(
                $"The trace was created for human side {recovered.Manifest.HumanSide}, not {_options.HumanSide}.");
        }

        if (_fullMatch is not null)
        {
            if (recovered.Manifest.Request is null
                || !string.Equals(
                    MatchRequestJson.Serialize(_fullMatch.Request),
                    MatchRequestJson.Serialize(recovered.Manifest.Request),
                    StringComparison.Ordinal))
            {
                throw new SessionTraceException("The recovered trace MatchRequest does not match the supplied match definition.");
            }
        }
        else if (!string.Equals(recovered.Manifest.ScenarioId, _document!.ScenarioId, StringComparison.Ordinal))
        {
            throw new SessionTraceException("The recovered trace scenario does not match the supplied scenario.");
        }

        SessionTraceAgentInfo? expectedAgent = DescribeAgent(_corpAgent);
        if (recovered.Manifest.Agent is { } recordedAgent
            && expectedAgent is { } currentAgent
            && (!string.Equals(recordedAgent.Type, currentAgent.Type, StringComparison.Ordinal)
                || !string.Equals(recordedAgent.Version, currentAgent.Version, StringComparison.Ordinal)
                || !string.Equals(recordedAgent.PolicyVersion, currentAgent.PolicyVersion, StringComparison.Ordinal)))
        {
            throw new SessionTraceException("The recovered trace opponent agent does not match the supplied agent.");
        }

        if (recovered.Policy is { } savedPolicy)
        {
            SoloAgentState requested = SoloAgentState.Capture(_corpAgent);
            if (BenchmarkHash.Value(savedPolicy.Configuration) != BenchmarkHash.Value(requested.Configuration)
                || savedPolicy.Kind != requested.Kind || savedPolicy.Version != requested.Version)
                throw new SessionTraceException("Recovered policy configuration differs from the supplied opponent.");
        }

        return recovered;
    }

    private void InitializeSessionTrace(GameState state)
    {
        if (string.IsNullOrWhiteSpace(_options.SessionTraceDirectory))
        {
            return;
        }

        _sessionTrace = SessionTrace.CreateNew(
            _options.SessionTraceDirectory,
            state,
            _options.HumanSide,
            _behaviorCatalog,
            _runtimeCardCatalog,
            _fullMatch?.Request,
            _document?.ScenarioId,
            DescribeAgent(_corpAgent),
            _corpAgent is OfflineAgent ? _committedAgent : null,
            _corpAgent is OfflineAgent && _replayInitial is not null
                ? new ReplayArtifact(_replayInitial, state.InitialSeed, _replayActions,
                    expectedFinalStateHash: DeterministicHash.ForState(state)) : null);
    }

    private static SessionTraceAgentInfo DescribeAgent(IGameAgent agent)
    {
        Type type = agent.GetType();
        string typeName = type.FullName ?? type.Name;
        string version = type.GetProperty("AgentVersion")?.GetValue(agent) as string
            ?? type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? type.Assembly.GetName().Version?.ToString()
            ?? "unknown";
        string? policyVersion = agent is FullGameContractAgent ? FullGameContractPolicy.Version : null;
        return new SessionTraceAgentInfo(typeName, version, policyVersion);
    }

    private async Task HandleSubmitAsync(SubmitCommand command)
    {
        if (_state is null)
        {
            command.Fail(new InvalidOperationException("The match has not been started."));
            return;
        }

        if (_recoveredPaused)
        {
            ApplyRejection rejection = new(
                ApplyRejectionCode.InvalidInputState,
                "The recovered match is paused; call ContinueRecoveredSessionAsync before submitting an action.");
            MatchSnapshot paused = Publish(
                Array.Empty<EngineEvent>(),
                new MatchFeedback(MatchFeedbackKind.Rejected, rejection.Message, rejection.Code),
                thinking: false);
            command.Complete(new MatchSubmissionResult(false, paused, rejection));
            return;
        }

        if (command.Submission.Actor != _state.Players.Single(player => player.Side == _options.HumanSide).Id)
        {
            ApplyRejection rejection = new(ApplyRejectionCode.WrongOwner, "Only the configured human side may submit client actions.");
            MatchSnapshot rejected = Publish(Array.Empty<EngineEvent>(),
                new MatchFeedback(MatchFeedbackKind.Rejected, rejection.Message, rejection.Code), thinking: false);
            command.Complete(new MatchSubmissionResult(false, rejected, rejection));
            return;
        }

        ApplyResult applied;
        try
        {
            applied = ApplyEngineAction(_state, command.Submission);
        }
        catch (SessionTraceException exception)
        {
            MatchSnapshot failed = Publish(
                Array.Empty<EngineEvent>(),
                new MatchFeedback(
                    MatchFeedbackKind.AgentFailure,
                    $"Durable session trace failed; the match stopped: {exception.Message}",
                    ApplyRejectionCode.InvalidInputState),
                thinking: false);
            command.Complete(new MatchSubmissionResult(
                false,
                failed,
                new ApplyRejection(ApplyRejectionCode.InvalidInputState, exception.Message)));
            throw;
        }
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
            command.Complete(ObservationBuilder.Build(_state, command.Viewer, _behaviorCatalog));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            command.Fail(exception);
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

        Observation observation = ObservationBuilder.Build(state, _options.HumanSide, _behaviorCatalog);
        IReadOnlyDictionary<CardInstanceId, string> cardKeys = _cardKeys.Update(state, _options.HumanSide);
        MatchViewModel view = ObservationViewMapper.Build(
            observation,
            _metadataResolver,
            _behaviorCatalog,
            handSizeProjection: null,
            presentationContext: MatchPresentationProjector.Build(state, _options.HumanSide, _behaviorCatalog, _runtimeCardCatalog));
        view = WithCardKeys(view, state, cardKeys, _currentCardKeys);
        // Engine events are authoritative diagnostics, but this publication is
        // the Runner-facing boundary. Never forward a physical Corp card ID
        // while that card is still hidden; omission is intentional for typed
        // payloads whose required ID cannot be safely redacted.
        IReadOnlyList<EngineEvent> visibleEvents = _options.HumanSide == PlayerSide.Runner
            ? RunnerEventVisibility.Filter(events, state) : CorpEventVisibility.Filter(events, state);
        IReadOnlyList<EngineEvent> frozenEvents = new ReadOnlyCollection<EngineEvent>(visibleEvents.ToArray());
        IReadOnlyList<MatchHistoryEntry> history = _restoredHistory ?? Array.Empty<MatchHistoryEntry>();
        _restoredHistory = null;
        if (_historyContext is { } context)
        {
            history = _historyProjector.Project(context.Before, view, state, events, context.Actor, context.Action);
            _historyContext = null;
        }
        IReadOnlyList<PresentationEvent> presentation = _presentationBefore is { } previous && events.Count > 0
            ? PresentationEventProjector.Project(previous, state, events, _options.HumanSide, _currentCardKeys, cardKeys, _behaviorCatalog)
            : Array.Empty<PresentationEvent>();
        _presentationBefore = null;
        _currentCardKeys = cardKeys;
        MatchSnapshot snapshot = new(
            Interlocked.Increment(ref _publicationSequence),
            observation,
            view,
            frozenEvents,
            feedback,
            thinking,
            _runOutcome)
        {
            ActionCount = state.ActionSequence,
            IsPaused = _recoveredPaused,
            History = history,
            PresentationEvents = presentation,
            TurnPreview = thinking ? null : PreviewNextTurnAction(state),
            SetupDecisionOwnerSide = state.PendingDecision is { Kind: DecisionKind.Setup } pending
                ? state.GetPlayer(pending.Owner).Side
                : null,
        };
        lock (_snapshotGate)
        {
            _currentSnapshot = snapshot;
        }

        // Snapshot consumers are UI/diagnostic observers outside the worker's
        // command pipeline. Invoke each delegate independently so one faulty
        // subscriber cannot prevent healthy subscribers from seeing the
        // publication or terminate the worker loop.
        Action<MatchSnapshot>? subscribers = SnapshotPublished;
        if (subscribers is not null)
        {
            foreach (Delegate subscriber in subscribers.GetInvocationList())
            {
                try
                {
                    ((Action<MatchSnapshot>)subscriber)(snapshot);
                }
                catch (Exception)
                {
                    // Subscriber failures must not affect match state or
                    // command completion. The publication is best-effort.
                }
            }
        }

        return snapshot;
    }

    private ApplyResult ApplyEngineAction(
        GameState state,
        ActionSubmission submission)
    {
        if (_traceFault is not null)
        {
            throw new SessionTraceException(
                "The session trace is faulted; no further actions are accepted.",
                _traceFault);
        }

        string? traceOperation = null;
        try
        {
            traceOperation = _sessionTrace?.AppendIntent(state, submission);
        }
        catch (Exception exception) when (_sessionTrace is not null)
        {
            SessionTraceException fault = exception as SessionTraceException
                ?? new SessionTraceException("The session trace could not record the action intent.", exception);
            _traceFault = fault;
            throw fault;
        }

        PlayerState? actor = state.Players.FirstOrDefault(player => player.Id == submission.Actor);
        Observation? actorView = actor is null
            ? null
            : ObservationBuilder.Build(state, actor.Side, _behaviorCatalog);
        LegalAction? action = actorView?.LegalActions.FirstOrDefault(candidate => candidate.Id == submission.ActionId);
        MatchViewModel before = ObservationViewMapper.Build(
            ObservationBuilder.Build(state, _options.HumanSide, _behaviorCatalog), _metadataResolver, _behaviorCatalog,
            handSizeProjection: null, presentationContext: MatchPresentationProjector.Build(state, _options.HumanSide, _behaviorCatalog, _runtimeCardCatalog));
        ApplyResult applied;
        try
        {
            applied = _runtimeCardCatalog is null
                ? GameEngine.Apply(state, submission, _behaviorCatalog)
                : GameEngine.Apply(state, submission, _behaviorCatalog, _runtimeCardCatalog);
            if (applied.Accepted)
            {
                AgentLifecycleHost.Accepted(_corpAgent, submission, applied.State, _behaviorCatalog);
                CacheCommittedAgent();
            }
            if (traceOperation is not null)
            {
                if (applied.Accepted)
                {
                    AcceptedActionRecord accepted = applied.AcceptedAction
                        ?? throw new InvalidOperationException("An accepted ApplyResult must contain an action record.");
                    _sessionTrace!.AppendAccepted(traceOperation, state, accepted,
                        _sessionTrace.Manifest.SchemaVersion == SessionTrace.PolicySchemaVersion ? _committedAgent : null);
                }
                else
                {
                    ApplyRejection rejection = applied.Rejection
                        ?? throw new InvalidOperationException("A rejected ApplyResult must contain a rejection.");
                    _sessionTrace!.AppendRejected(traceOperation, state, submission, rejection);
                }
            }
        }
        catch (Exception exception) when (_sessionTrace is not null)
        {
            SessionTraceException fault = exception as SessionTraceException
                ?? new SessionTraceException("The session trace could not commit the action result.", exception);
            _traceFault = fault;
            throw fault;
        }

        if (applied.Accepted && actorView is not null && action is not null)
            _historyContext = (before, actorView.Viewer, action);
        if (applied.Accepted)
            _presentationBefore = state;
        if (applied.Accepted && _replayInitial is not null)
            _replayActions.Add(ReplayAction.From(applied.AcceptedAction!));
        if (!applied.Accepted && actor?.Side != _options.HumanSide)
            AgentLifecycleHost.Discard(_corpAgent);
        return applied;
    }

    /// <summary>Adds presentation keys per server position and the tracked keys of newly revealed cards.</summary>
    private static MatchViewModel WithCardKeys(MatchViewModel view, GameState state,
        IReadOnlyDictionary<CardInstanceId, string> keys, IReadOnlyDictionary<CardInstanceId, string> previous)
    {
        string? Key(CardInstanceId id) => keys.TryGetValue(id, out string? key) ? key : null;
        Dictionary<string, ServerState> servers = state.Servers.ToDictionary(server => server.Id.Value, StringComparer.Ordinal);
        var revealed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((CardInstanceId id, string key) in keys)
        {
            if (key == id.Value && previous.TryGetValue(id, out string? before)
                && before.StartsWith(CardKeyTracker.TrackedPrefix, StringComparison.Ordinal))
                revealed[key] = before;
        }
        return view with
        {
            Servers = [.. view.Servers.Select(server => servers.TryGetValue(server.Id, out ServerState? authoritative)
                ? server with
                {
                    // The view orders ICE outermost first; Core stores it innermost first.
                    IceKeys = [.. state.GetZone(authoritative.IceZoneId).CardIds.Select(Key).Reverse()],
                    RootKeys = [.. state.GetZone(authoritative.RootZoneId).CardIds.Select(Key)],
                }
                : server)],
            RevealedFrom = revealed,
            AdvancementByKey = state.Cards.Where(card => card.AdvancementCounters > 0 && keys.ContainsKey(card.Id)
                    && state.GetZone(card.ZoneId).Kind is ZoneKind.ServerRoot or ZoneKind.ServerIce)
                .ToDictionary(card => keys[card.Id], card => card.AdvancementCounters, StringComparer.Ordinal),
        };
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
        cancellationToken.ThrowIfCancellationRequested();

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

    private sealed class ContinueRecoveredCommand : CoordinatorCommand<MatchSnapshot>
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
