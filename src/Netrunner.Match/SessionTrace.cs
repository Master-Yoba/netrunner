using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Persistent metadata describing one coordinator session trace.</summary>
public sealed record SessionTraceManifest
{
    [System.Text.Json.Serialization.JsonConstructor]
    public SessionTraceManifest(
        int schemaVersion,
        string traceKind,
        string traceId,
        PlayerSide humanSide,
        ulong seed,
        string rulesVersion,
        string cardDataVersion,
        string behaviorRegistryVersion,
        string? behaviorCatalogFingerprint,
        string initialStateHash,
        SessionTraceBuildInfo build,
        MatchRequest? request = null,
        string? scenarioId = null,
        SessionTraceAgentInfo? agent = null)
    {
        ArgumentNullException.ThrowIfNull(traceKind);
        ArgumentNullException.ThrowIfNull(traceId);
        ArgumentNullException.ThrowIfNull(rulesVersion);
        ArgumentNullException.ThrowIfNull(cardDataVersion);
        ArgumentNullException.ThrowIfNull(behaviorRegistryVersion);
        ArgumentNullException.ThrowIfNull(initialStateHash);
        ArgumentNullException.ThrowIfNull(build);
        if (!Enum.IsDefined(humanSide))
        {
            throw new ArgumentOutOfRangeException(nameof(humanSide));
        }

        SchemaVersion = schemaVersion;
        TraceKind = traceKind;
        TraceId = traceId;
        HumanSide = humanSide;
        Seed = seed;
        RulesVersion = rulesVersion;
        CardDataVersion = cardDataVersion;
        BehaviorRegistryVersion = behaviorRegistryVersion;
        BehaviorCatalogFingerprint = behaviorCatalogFingerprint;
        InitialStateHash = initialStateHash;
        Build = build;
        Request = request;
        ScenarioId = scenarioId;
        Agent = agent;
    }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public int SchemaVersion { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string TraceKind { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string TraceId { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public PlayerSide HumanSide { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public ulong Seed { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string RulesVersion { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string CardDataVersion { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string BehaviorRegistryVersion { get; private set; }

    public string? BehaviorCatalogFingerprint { get; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string InitialStateHash { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public SessionTraceBuildInfo Build { get; private set; }

    /// <summary>The complete composition request when this is a full match.</summary>
    public MatchRequest? Request { get; }

    /// <summary>The M2 scenario identity when this is a scenario-backed match.</summary>
    public string? ScenarioId { get; }

    /// <summary>Opponent policy provenance, when the coordinator can identify it.</summary>
    public SessionTraceAgentInfo? Agent { get; }
}

/// <summary>Stable opponent agent and policy provenance recorded in a trace.</summary>
public sealed record SessionTraceAgentInfo
{
    [System.Text.Json.Serialization.JsonConstructor]
    public SessionTraceAgentInfo(string type, string version, string? policyVersion = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(version);
        Type = type;
        Version = version;
        PolicyVersion = policyVersion;
    }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string Type { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string Version { get; private set; }

    public string? PolicyVersion { get; }
}

/// <summary>Build provenance captured with a trace, including the loaded engine bytes.</summary>
public sealed record SessionTraceBuildInfo
{
    [System.Text.Json.Serialization.JsonConstructor]
    public SessionTraceBuildInfo(
        string engineAssemblyName,
        string engineAssemblyVersion,
        string engineAssemblySha256,
        string? engineAssemblyLocation,
        string matchAssemblyName,
        string matchAssemblyVersion,
        string matchAssemblySha256,
        string? matchAssemblyLocation)
    {
        ArgumentNullException.ThrowIfNull(engineAssemblyName);
        ArgumentNullException.ThrowIfNull(engineAssemblyVersion);
        ArgumentNullException.ThrowIfNull(engineAssemblySha256);
        ArgumentNullException.ThrowIfNull(matchAssemblyName);
        ArgumentNullException.ThrowIfNull(matchAssemblyVersion);
        ArgumentNullException.ThrowIfNull(matchAssemblySha256);
        if (!DeterministicHash.IsSha256Hex(engineAssemblySha256))
        {
            throw new ArgumentException("The engine assembly hash must be lowercase SHA-256 hex.", nameof(engineAssemblySha256));
        }

        if (!DeterministicHash.IsSha256Hex(matchAssemblySha256))
        {
            throw new ArgumentException("The Match assembly hash must be lowercase SHA-256 hex.", nameof(matchAssemblySha256));
        }

        EngineAssemblyName = engineAssemblyName;
        EngineAssemblyVersion = engineAssemblyVersion;
        EngineAssemblySha256 = engineAssemblySha256;
        EngineAssemblyLocation = engineAssemblyLocation;
        MatchAssemblyName = matchAssemblyName;
        MatchAssemblyVersion = matchAssemblyVersion;
        MatchAssemblySha256 = matchAssemblySha256;
        MatchAssemblyLocation = matchAssemblyLocation;
    }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string EngineAssemblyName { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string EngineAssemblyVersion { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string EngineAssemblySha256 { get; private set; }

    public string? EngineAssemblyLocation { get; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string MatchAssemblyName { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string MatchAssemblyVersion { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string MatchAssemblySha256 { get; private set; }

    public string? MatchAssemblyLocation { get; }
}

/// <summary>Result of recovering a verified trace state.</summary>
public sealed record SessionRecoveryResult
{
    internal SessionRecoveryResult(
        GameState state,
        ReplayArtifact replay,
        SessionTraceManifest manifest,
        string directoryPath,
        bool hasInterruptedAction,
        bool ignoredIncompleteTail, SoloAgentState? policy = null, ReplayArtifact? sessionReplay = null)
    {
        State = state;
        Replay = replay;
        Manifest = manifest;
        DirectoryPath = directoryPath;
        HasInterruptedAction = hasInterruptedAction;
        IgnoredIncompleteTail = ignoredIncompleteTail;
        Policy = policy;
        SessionReplay = sessionReplay ?? replay;
    }

    public SoloAgentState? Policy { get; }

    public ReplayArtifact SessionReplay { get; }

    public GameState State { get; }

    public ReplayArtifact Replay { get; }

    public SessionTraceManifest Manifest { get; }

    public string DirectoryPath { get; }

    /// <summary>True when the journal ends after an intent without a completion.</summary>
    public bool HasInterruptedAction { get; }

    /// <summary>True when an incomplete final JSONL record was ignored safely.</summary>
    public bool IgnoredIncompleteTail { get; }
}

/// <summary>Raised when a session trace is incomplete, corrupt, or incompatible.</summary>
public sealed class SessionTraceException : IOException
{
    public SessionTraceException(string message)
        : base(message)
    {
    }

    public SessionTraceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Durable journal record kinds.</summary>
public enum SessionTraceRecordKind
{
    Intent,
    Completion,
}

/// <summary>
/// One JSONL journal entry. An intent is flushed before the engine reducer is
/// called; a completion is flushed only after the reducer returns.
/// </summary>
public sealed record SessionTraceJournalRecord
{
    [System.Text.Json.Serialization.JsonConstructor]
    public SessionTraceJournalRecord(
        string operationId,
        SessionTraceRecordKind kind,
        long actionSequence,
        ActionSubmission submission,
        string beforeStateHash,
        ReplayAction? acceptedAction = null,
        ApplyRejection? rejection = null)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(beforeStateHash);
        if (!DeterministicHash.IsSha256Hex(beforeStateHash))
        {
            throw new ArgumentException("The before-state hash must be lowercase SHA-256 hex.", nameof(beforeStateHash));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(actionSequence);

        if (kind == SessionTraceRecordKind.Intent && (acceptedAction is not null || rejection is not null))
        {
            throw new ArgumentException("An intent cannot contain a completion.", nameof(kind));
        }

        if (kind == SessionTraceRecordKind.Completion && acceptedAction is null && rejection is null)
        {
            throw new ArgumentException("A completion must contain an accepted action or rejection.", nameof(kind));
        }

        OperationId = operationId;
        Kind = kind;
        ActionSequence = actionSequence;
        Submission = submission;
        BeforeStateHash = beforeStateHash;
        AcceptedAction = acceptedAction;
        Rejection = rejection;
    }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string OperationId { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public SessionTraceRecordKind Kind { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public long ActionSequence { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public ActionSubmission Submission { get; private set; }

    [System.Text.Json.Serialization.JsonInclude]
    [System.Text.Json.Serialization.JsonRequired]
    public string BeforeStateHash { get; private set; }

    public ReplayAction? AcceptedAction { get; }

    public ApplyRejection? Rejection { get; }
}

/// <summary>
/// Durable Match-layer session trace. The replay document is the resumable
/// state; the JSONL journal provides intent/completion evidence for crash
/// diagnostics and never crosses the presentation boundary.
/// </summary>
public sealed class SessionTrace : IDisposable
{
    public const int CurrentSchemaVersion = 1;
    public const int PolicySchemaVersion = 2;
    public const string PolicyCheckpointFileName = "policy-checkpoint.json";
    public const string TraceKind = "match-session";
    public const string ManifestFileName = "manifest.json";
    public const string JournalFileName = "journal.jsonl";
    public const string ReplayFileName = "replay.json";

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly SessionTraceManifest _manifest;
    private readonly GameSnapshot _initialSnapshot;
    private readonly FileStream _journal;
    private readonly List<ReplayAction> _actions;
    private readonly List<SessionPolicyBoundary> _policyBoundaries = [];
    private ReplayArtifact? _sessionPrefix;
    private bool _disposed;

    private SessionTrace(
        string directory,
        SessionTraceManifest manifest,
        ReplayArtifact replay,
        FileStream journal)
    {
        _directory = directory;
        _manifest = manifest;
        _initialSnapshot = replay.InitialSnapshot;
        _journal = journal;
        _actions = replay.Actions.ToList();
    }

    public string DirectoryPath => _directory;

    public SessionTraceManifest Manifest => _manifest;

    public static SessionTrace CreateNew(
        string directory,
        GameState initialState,
        PlayerSide humanSide,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog = null,
        MatchRequest? request = null,
        string? scenarioId = null,
        SessionTraceAgentInfo? agent = null,
        SoloAgentState? policy = null, ReplayArtifact? sessionPrefix = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        if (!Enum.IsDefined(humanSide))
        {
            throw new ArgumentOutOfRangeException(nameof(humanSide));
        }

        string fullDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullDirectory);
        string manifestPath = Path.Combine(fullDirectory, ManifestFileName);
        string replayPath = Path.Combine(fullDirectory, ReplayFileName);
        string journalPath = Path.Combine(fullDirectory, JournalFileName);
        if (File.Exists(manifestPath) || File.Exists(replayPath) || File.Exists(journalPath)
            || File.Exists(Path.Combine(fullDirectory, PolicyCheckpointFileName)))
        {
            throw new SessionTraceException($"Session trace directory '{fullDirectory}' already contains trace files.");
        }

        GameSnapshot snapshot = runtimeCardCatalog is null
            ? initialState.CreateSnapshot(behaviorCatalog)
            : initialState.CreateSnapshot(behaviorCatalog, runtimeCardCatalog);
        string initialHash = DeterministicHash.ForState(snapshot.ToState());
        if (request is not null && request.Seed != initialState.InitialSeed)
        {
            throw new SessionTraceException("The trace request seed does not match the initial state seed.");
        }

        if (policy is not null)
        {
            if (runtimeCardCatalog is null) throw new SessionTraceException("Policy traces require the runtime catalog.");
            policy.ValidateBoundary(initialState, behaviorCatalog, runtimeCardCatalog);
        }
        SessionTraceManifest manifest = new(
            policy is null ? CurrentSchemaVersion : PolicySchemaVersion,
            TraceKind,
            Guid.NewGuid().ToString("N"),
            humanSide,
            initialState.InitialSeed,
            initialState.RulesVersion,
            initialState.CardDataVersion,
            initialState.BehaviorRegistryVersion,
            initialState.BehaviorCatalogFingerprint,
            initialHash,
            CreateBuildInfo(),
            request,
            scenarioId,
            agent);
        ReplayArtifact replay = new(
            snapshot,
            initialState.InitialSeed,
            expectedFinalStateHash: initialHash,
            behaviorCatalogFingerprint: initialState.BehaviorCatalogFingerprint,
            rulesVersion: initialState.RulesVersion,
            cardDataVersion: initialState.CardDataVersion,
            behaviorRegistryVersion: initialState.BehaviorRegistryVersion);

        WriteAtomic(manifestPath, EngineJson.SerializeUtf8(manifest));
        WriteAtomic(replayPath, EngineJson.SerializeUtf8(replay));
        FileStream journal = new(
            journalPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read,
            4096,
            FileOptions.WriteThrough);
        journal.Flush(true);
        var trace = new SessionTrace(fullDirectory, manifest, replay, journal) { _sessionPrefix = sessionPrefix };
        if (policy is not null)
        {
            trace._policyBoundaries.Add(new(initialState.ActionSequence, initialHash, policy));
            try { trace.WritePolicyCheckpoint(replay); }
            catch { trace.Dispose(); throw; }
        }
        return trace;
    }

    /// <summary>Reads and validates only the public trace manifest.</summary>
    public static SessionTraceManifest ReadManifest(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string path = Path.Combine(Path.GetFullPath(directory), ManifestFileName);
        try
        {
            return EngineJson.Deserialize<SessionTraceManifest>(File.ReadAllText(path));
        }
        catch (SessionTraceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            throw new SessionTraceException($"The session trace manifest '{path}' is unreadable.", exception);
        }
    }

    /// <summary>
    /// Verifies the atomic replay document against the supplied immutable
    /// catalogs and returns the last accepted state (or an exact earlier
    /// action sequence when requested).
    /// </summary>
    public static SessionRecoveryResult Recover(
        string directory,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog = null,
        long? throughActionSequence = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        string fullDirectory = Path.GetFullPath(directory);
        SessionTraceManifest manifest = ReadManifest(fullDirectory);
        string replayPath = Path.Combine(fullDirectory, ReplayFileName);
        ReplayArtifact replay;
        SessionPolicyCheckpoint? checkpoint = null;
        try
        {
            if (manifest.SchemaVersion == PolicySchemaVersion)
            {
                if (runtimeCardCatalog is null) throw new SessionTraceException("Policy traces require the runtime catalog.");
                checkpoint = EngineJson.Deserialize<SessionPolicyCheckpoint>(
                    File.ReadAllText(Path.Combine(fullDirectory, PolicyCheckpointFileName)));
                checkpoint.Validate(manifest, behaviorCatalog, runtimeCardCatalog);
                replay = checkpoint.Replay;
            }
            else replay = EngineJson.Deserialize<ReplayArtifact>(
                File.ReadAllText(replayPath),
                behaviorCatalog,
                runtimeCardCatalog);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            throw new SessionTraceException($"The session trace replay '{replayPath}' is unreadable.", exception);
        }

        ValidateManifest(manifest, replay, runtimeCardCatalog);
        JournalAudit audit = AuditJournal(fullDirectory, replay);
        ReplayVerificationResult fullVerification = runtimeCardCatalog is null
            ? ReplayVerifier.Verify(replay, behaviorCatalog)
            : ReplayVerifier.Verify(replay, behaviorCatalog, runtimeCardCatalog);
        if (!fullVerification.Success || fullVerification.FinalState is null)
        {
            throw new SessionTraceException($"Session trace replay verification failed: {fullVerification.Message}");
        }

        ReplayArtifact selected = SelectReplay(replay, throughActionSequence);
        ReplayVerificationResult verified = runtimeCardCatalog is null
            ? ReplayVerifier.Verify(selected, behaviorCatalog)
            : ReplayVerifier.Verify(selected, behaviorCatalog, runtimeCardCatalog);
        if (!verified.Success || verified.FinalState is null)
        {
            throw new SessionTraceException($"Session trace replay verification failed: {verified.Message}");
        }

        return new SessionRecoveryResult(
            verified.FinalState,
            selected,
            manifest,
            fullDirectory,
            audit.HasInterruptedAction,
            audit.IgnoredIncompleteTail,
            checkpoint?.Boundaries.Single(boundary => boundary.ActionSequence == verified.FinalState.ActionSequence).Agent,
            checkpoint is null ? selected : SelectReplay(checkpoint.SessionReplay, throughActionSequence));
    }

    /// <summary>Records an action intent and flushes it before Apply is called.</summary>
    public string AppendIntent(GameState state, ActionSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(submission);
        lock (_gate)
        {
            ThrowIfDisposed();
            string operationId = Guid.NewGuid().ToString("N");
            AppendJournal(new SessionTraceJournalRecord(
                operationId,
                SessionTraceRecordKind.Intent,
                checked(state.ActionSequence + 1),
                submission,
                DeterministicHash.ForState(state)));
            return operationId;
        }
    }

    /// <summary>
    /// Commits an accepted Apply result to replay.json and then flushes its
    /// completion journal record. The caller must publish only after return.
    /// </summary>
    public void AppendAccepted(string operationId, GameState before, AcceptedActionRecord accepted, SoloAgentState? policy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(accepted);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (accepted.Sequence != before.ActionSequence + 1)
            {
                throw new SessionTraceException("The accepted action sequence does not follow the traced state.");
            }

            if (_manifest.SchemaVersion == PolicySchemaVersion && policy is null)
                throw new SessionTraceException("A policy trace requires the committed policy with every accepted action.");
            if (policy is not null && _manifest.SchemaVersion != PolicySchemaVersion)
                throw new SessionTraceException("A legacy trace cannot acquire a stateful policy mid-session.");
            if (policy is not null) _policyBoundaries.Add(new(accepted.Sequence, accepted.ResultingStateHash, policy));
            ReplayAction replayAction = ReplayAction.From(accepted);
            _actions.Add(replayAction);
            try
            {
                WriteReplay(_actions, accepted.ResultingStateHash);
                AppendJournal(new SessionTraceJournalRecord(
                    operationId,
                    SessionTraceRecordKind.Completion,
                    accepted.Sequence,
                    accepted.Submission,
                    DeterministicHash.ForState(before),
                    replayAction));
            }
            catch
            {
                _actions.RemoveAt(_actions.Count - 1);
                if (policy is not null) _policyBoundaries.RemoveAt(_policyBoundaries.Count - 1);
                throw;
            }
        }
    }

    /// <summary>Flushes a rejected Apply result without changing replay state.</summary>
    public void AppendRejected(string operationId, GameState before, ActionSubmission submission, ApplyRejection rejection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(rejection);
        lock (_gate)
        {
            ThrowIfDisposed();
            AppendJournal(new SessionTraceJournalRecord(
                operationId,
                SessionTraceRecordKind.Completion,
                checked(before.ActionSequence + 1),
                submission,
                DeterministicHash.ForState(before),
                rejection: rejection));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _journal.Flush(true);
            _journal.Dispose();
        }
    }

    private void WriteReplay(IReadOnlyList<ReplayAction> actions, string finalStateHash)
    {
        ReplayArtifact replay = new(
            _initialSnapshot,
            _manifest.Seed,
            actions,
            _manifest.RulesVersion,
            _manifest.CardDataVersion,
            _manifest.BehaviorRegistryVersion,
            finalStateHash,
            _manifest.BehaviorCatalogFingerprint);
        if (_manifest.SchemaVersion == PolicySchemaVersion) WritePolicyCheckpoint(replay);
        WriteAtomic(Path.Combine(_directory, ReplayFileName), EngineJson.SerializeUtf8(replay));
    }

    private void WritePolicyCheckpoint(ReplayArtifact replay)
    {
        ReplayArtifact session = _sessionPrefix is null ? replay : new ReplayArtifact(_sessionPrefix.InitialSnapshot,
            replay.Seed, _sessionPrefix.Actions.Concat(replay.Actions).ToArray(), expectedFinalStateHash: replay.ExpectedFinalStateHash);
        WriteAtomic(Path.Combine(_directory, PolicyCheckpointFileName),
            EngineJson.SerializeUtf8(SessionPolicyCheckpoint.Create(replay, session, _policyBoundaries)));
    }

    private void AppendJournal(SessionTraceJournalRecord record)
    {
        byte[] encoded = CanonicalJson.SerializeUtf8(record);
        _journal.Position = _journal.Length;
        _journal.Write(encoded);
        _journal.WriteByte((byte)'\n');
        _journal.Flush(true);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static ReplayArtifact SelectReplay(ReplayArtifact replay, long? throughActionSequence)
    {
        if (throughActionSequence is null)
        {
            return replay;
        }

        long sequence = throughActionSequence.Value;
        long initialSequence = replay.InitialSnapshot.ToState().ActionSequence;
        if (sequence < initialSequence)
        {
            throw new SessionTraceException(
                $"Requested action sequence {sequence} precedes the initial snapshot sequence {initialSequence}.");
        }

        ReplayAction[] actions = replay.Actions.Where(action => action.Sequence <= sequence).ToArray();
        if (actions.Length > 0 && actions[^1].Sequence != sequence
            || actions.Length == 0 && sequence != initialSequence)
        {
            throw new SessionTraceException($"Requested action sequence {sequence} is not a durable decision boundary.");
        }

        return new ReplayArtifact(
            replay.InitialSnapshot,
            replay.Seed,
            actions,
            replay.RulesVersion,
            replay.CardDataVersion,
            replay.BehaviorRegistryVersion,
            expectedFinalStateHash: null,
            replay.BehaviorCatalogFingerprint);
    }

    private static void ValidateManifest(
        SessionTraceManifest manifest,
        ReplayArtifact replay,
        IRuntimeCardCatalog? runtimeCardCatalog)
    {
        if (manifest.SchemaVersion is not (CurrentSchemaVersion or PolicySchemaVersion) || !string.Equals(manifest.TraceKind, TraceKind, StringComparison.Ordinal))
        {
            throw new SessionTraceException("The session trace manifest schema or kind is unsupported.");
        }

        GameState initial = replay.InitialSnapshot.ToState();
        if ((initial.RuntimeCardCatalogVersion is not null || initial.RuntimeCardCatalogFingerprint is not null)
            && runtimeCardCatalog is null)
        {
            throw new SessionTraceException("The trace requires its pinned runtime card catalog for recovery.");
        }

        if (runtimeCardCatalog is not null
            && (initial.RuntimeCardCatalogVersion is not null || initial.RuntimeCardCatalogFingerprint is not null)
            && (!string.Equals(initial.RuntimeCardCatalogVersion, runtimeCardCatalog.Version, StringComparison.Ordinal)
                || !string.Equals(initial.RuntimeCardCatalogFingerprint, runtimeCardCatalog.Fingerprint, StringComparison.Ordinal)))
        {
            throw new SessionTraceException("The supplied runtime card catalog does not match the trace state pin.");
        }
        if (manifest.Seed != replay.Seed || manifest.Seed != initial.InitialSeed)
        {
            throw new SessionTraceException("The session trace seed does not match the replay and initial snapshot.");
        }

        if (!string.Equals(manifest.InitialStateHash, DeterministicHash.ForState(initial), StringComparison.Ordinal))
        {
            throw new SessionTraceException("The session trace initial state hash does not match replay.json.");
        }

        if (!string.Equals(manifest.RulesVersion, replay.RulesVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.CardDataVersion, replay.CardDataVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.BehaviorRegistryVersion, replay.BehaviorRegistryVersion, StringComparison.Ordinal)
            || !string.Equals(manifest.BehaviorCatalogFingerprint, replay.BehaviorCatalogFingerprint, StringComparison.Ordinal)
            || !string.Equals(replay.RulesVersion, initial.RulesVersion, StringComparison.Ordinal)
            || !string.Equals(replay.CardDataVersion, initial.CardDataVersion, StringComparison.Ordinal)
            || !string.Equals(replay.BehaviorRegistryVersion, initial.BehaviorRegistryVersion, StringComparison.Ordinal)
            || !string.Equals(replay.BehaviorCatalogFingerprint, initial.BehaviorCatalogFingerprint, StringComparison.Ordinal))
        {
            throw new SessionTraceException("The session trace manifest pins do not match replay.json.");
        }

        if (manifest.Request is not null
            && (manifest.Request.Seed != manifest.Seed
                || !string.Equals(manifest.Request.RulesVersion, manifest.RulesVersion, StringComparison.Ordinal)
                || !string.Equals(manifest.Request.CardDataVersion, manifest.CardDataVersion, StringComparison.Ordinal)))
        {
            throw new SessionTraceException("The session trace MatchRequest metadata does not match the manifest.");
        }

        if (replay.ExpectedFinalStateHash is null)
        {
            throw new SessionTraceException("The session trace replay has no durable final state hash.");
        }
    }

    private static JournalAudit AuditJournal(string directory, ReplayArtifact replay)
    {
        string path = Path.Combine(directory, JournalFileName);
        if (!File.Exists(path))
        {
            throw new SessionTraceException($"Session trace journal '{path}' is missing.");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SessionTraceException($"The session trace journal '{path}' is unreadable.", exception);
        }

        SessionTraceJournalRecord? pending = null;
        bool ignoredTail = false;
        GameState initial = replay.InitialSnapshot.ToState();
        long expectedSequence = checked(initial.ActionSequence + 1);
        string expectedBeforeHash = DeterministicHash.ForState(initial);
        int replayIndex = 0;
        int offset = 0;
        while (offset < bytes.Length)
        {
            int newline = Array.IndexOf(bytes, (byte)'\n', offset);
            bool finalSegment = newline < 0;
            int end = finalSegment ? bytes.Length : newline;
            ReadOnlySpan<byte> line = bytes.AsSpan(offset, end - offset);
            offset = finalSegment ? bytes.Length : newline + 1;
            if (finalSegment)
            {
                // The writer always terminates durable JSONL records. Any
                // bytes after the last newline are an interrupted tail,
                // including a syntactically complete but unflushed object.
                ignoredTail = true;
                break;
            }

            if (line.IsEmpty)
            {
                throw new SessionTraceException("The session trace journal contains an empty record.");
            }

            SessionTraceJournalRecord record;
            try
            {
                record = EngineJson.Deserialize<SessionTraceJournalRecord>(line);
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
            {
                if (finalSegment)
                {
                    ignoredTail = true;
                    break;
                }

                throw new SessionTraceException("The session trace journal contains a corrupt complete record.", exception);
            }

            if (record.Kind == SessionTraceRecordKind.Intent)
            {
                if (pending is not null)
                {
                    throw new SessionTraceException("The session trace journal contains two intents without a completion.");
                }

                if (record.ActionSequence != expectedSequence
                    || !string.Equals(record.BeforeStateHash, expectedBeforeHash, StringComparison.Ordinal))
                {
                    throw new SessionTraceException("The session trace intent does not follow the durable state hash.");
                }

                pending = record;
                continue;
            }

            if (record.Kind != SessionTraceRecordKind.Completion || pending is null
                || !string.Equals(record.OperationId, pending.OperationId, StringComparison.Ordinal)
                || record.ActionSequence != pending.ActionSequence
                || record.Submission != pending.Submission
                || !string.Equals(record.BeforeStateHash, pending.BeforeStateHash, StringComparison.Ordinal))
            {
                throw new SessionTraceException("The session trace journal contains an unmatched completion.");
            }

            bool hasAccepted = record.AcceptedAction is not null;
            bool hasRejection = record.Rejection is not null;
            if (hasAccepted == hasRejection)
            {
                throw new SessionTraceException("A completion must contain exactly one accepted action or rejection.");
            }

            if (record.AcceptedAction is { } accepted)
            {
                if (accepted.Sequence != record.ActionSequence || accepted.Submission != record.Submission
                    || replayIndex >= replay.Actions.Count
                    || !EquivalentReplayAction(accepted, replay.Actions[replayIndex]))
                {
                    throw new SessionTraceException("The session trace journal and replay.json disagree on an accepted action.");
                }

                replayIndex++;
                expectedBeforeHash = accepted.ExpectedStateHash
                    ?? throw new SessionTraceException("An accepted action is missing its resulting state hash.");
                expectedSequence = checked(accepted.Sequence + 1);
            }

            pending = null;
        }

        bool interrupted = pending is not null || ignoredTail;
        if (pending is not null && replayIndex < replay.Actions.Count)
        {
            ReplayAction replayAction = replay.Actions[replayIndex];
            if (EquivalentSubmission(pending.Submission, replayAction.Submission)
                && pending.ActionSequence == replayAction.Sequence
                && string.Equals(pending.BeforeStateHash, expectedBeforeHash, StringComparison.Ordinal))
            {
                replayIndex++;
                expectedBeforeHash = replayAction.ExpectedStateHash
                    ?? throw new SessionTraceException("An accepted action is missing its resulting state hash.");
                expectedSequence = checked(replayAction.Sequence + 1);
            }
        }

        if (replayIndex != replay.Actions.Count)
        {
            throw new SessionTraceException("The session trace replay has no matching journal evidence.");
        }

        return new JournalAudit(interrupted, ignoredTail);

        static bool EquivalentReplayAction(ReplayAction left, ReplayAction right) =>
            left.Sequence == right.Sequence
            && EquivalentSubmission(left.Submission, right.Submission)
            && string.Equals(left.ExpectedStateHash, right.ExpectedStateHash, StringComparison.Ordinal)
            && string.Equals(left.ExpectedEventBatchHash, right.ExpectedEventBatchHash, StringComparison.Ordinal);

        static bool EquivalentSubmission(ActionSubmission left, ActionSubmission right) =>
            left == right;
    }

    private static SessionTraceBuildInfo CreateBuildInfo()
    {
        Assembly engine = typeof(GameState).Assembly;
        Assembly match = typeof(SessionTrace).Assembly;
        return new SessionTraceBuildInfo(
            engine.GetName().Name ?? "unknown",
            InformationalVersion(engine),
            AssemblySha256(engine),
            SafeLocation(engine),
            match.GetName().Name ?? "unknown",
            InformationalVersion(match),
            AssemblySha256(match),
            SafeLocation(match));

        static string InformationalVersion(Assembly assembly) =>
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        static string? SafeLocation(Assembly assembly) =>
            string.IsNullOrWhiteSpace(assembly.Location) ? null : assembly.Location;

        static string AssemblySha256(Assembly assembly)
        {
            string? path = SafeLocation(assembly);
            byte[] bytes = path is not null && File.Exists(path)
                ? File.ReadAllBytes(path)
                : Encoding.UTF8.GetBytes(assembly.FullName ?? assembly.GetName().Name ?? "unknown");
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        try { AtomicFile.Write(path, bytes); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SessionTraceException($"Could not durably replace '{path}'.", exception);
        }
    }

    private sealed record JournalAudit(bool HasInterruptedAction, bool IgnoredIncompleteTail);
}
