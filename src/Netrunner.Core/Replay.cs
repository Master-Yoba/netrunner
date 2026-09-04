using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

public static class EngineArtifactKinds
{
    public const string Replay = "replay";
    public const string Scenario = "scenario";
}

public static class EngineCompatibility
{
    public const int ArtifactSchemaVersion = 1;
    // Slice 4.5 persists targeted PlayCard payloads and explicit run-event
    // breach-modifier provenance in live RunState snapshots.
    public const int StateSchemaVersion = 11;
    public const string RulesVersion = "26.03";
    public const string CardDataVersion = "fixture-1";
    /// <summary>
    /// Authenticated System Gateway metadata snapshot used by the provisioned
    /// M2 client lane. This is deliberately a source commit pin, not a
    /// behavior-registry version.
    /// </summary>
    public const string GatewayCardDataVersion = "236140cf84491c23995b028bc791281837520b97";
    // M1 fixture states retain their fixture behavior identity. Gateway/M2
    // composition opts into this explicit legacy behavior pin when reading
    // the M2 scenario contract; it is not sourced from a process registry.
    public const string BehaviorRegistryVersion = "fixture-1";
    public const string GatewayBehaviorRegistryVersion = "gateway-m2-3";
}

/// <summary>
/// One accepted action in a replay. The submission is the replay input; the
/// optional hashes are evidence captured from the corresponding ApplyResult.
/// </summary>
public sealed record ReplayAction
{
    [JsonConstructor]
    public ReplayAction(
        long sequence,
        ActionSubmission submission,
        string? expectedStateHash = null,
        string? expectedEventBatchHash = null)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Replay action sequence must be positive.");
        }

        ValidateOptionalHash(expectedStateHash, nameof(expectedStateHash));
        ValidateOptionalHash(expectedEventBatchHash, nameof(expectedEventBatchHash));
        Sequence = sequence;
        Submission = submission;
        ExpectedStateHash = expectedStateHash;
        ExpectedEventBatchHash = expectedEventBatchHash;
    }

    [JsonInclude]
    [JsonRequired]
    public long Sequence { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ActionSubmission Submission { get; private set; }

    public string? ExpectedStateHash { get; }

    public string? ExpectedEventBatchHash { get; }

    public static ReplayAction From(AcceptedActionRecord record) =>
        new(
            record.Sequence,
            record.Submission,
            record.ResultingStateHash,
            record.ResultingEventBatchHash);

    private static void ValidateOptionalHash(string? value, string parameterName)
    {
        if (value is not null && !DeterministicHash.IsSha256Hex(value))
        {
            throw new ArgumentException("Replay hashes must be lowercase SHA-256 hex.", parameterName);
        }
    }
}

/// <summary>Deterministic replay document. It contains no creation timestamp.</summary>
public sealed class ReplayArtifact
{
    [JsonConstructor]
    public ReplayArtifact(
        int schemaVersion,
        string artifactKind,
        string rulesVersion,
        string cardDataVersion,
        string behaviorRegistryVersion,
        ulong seed,
        GameSnapshot initialSnapshot,
        IReadOnlyList<ReplayAction>? actions = null,
        string? expectedFinalStateHash = null,
        string? behaviorCatalogFingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(artifactKind);
        ArgumentNullException.ThrowIfNull(rulesVersion);
        ArgumentNullException.ThrowIfNull(cardDataVersion);
        ArgumentNullException.ThrowIfNull(behaviorRegistryVersion);
        ArgumentNullException.ThrowIfNull(initialSnapshot);
        ArgumentNullException.ThrowIfNull(actions);

        if (expectedFinalStateHash is not null && !DeterministicHash.IsSha256Hex(expectedFinalStateHash))
        {
            throw new ArgumentException("Expected final state hash must be lowercase SHA-256 hex.", nameof(expectedFinalStateHash));
        }

        if (behaviorCatalogFingerprint is not null
            && !DeterministicHash.IsSha256Hex(behaviorCatalogFingerprint))
        {
            throw new ArgumentException("Behavior catalog fingerprints must be lowercase SHA-256 hex.", nameof(behaviorCatalogFingerprint));
        }

        SchemaVersion = schemaVersion;
        ArtifactKind = artifactKind;
        RulesVersion = rulesVersion;
        CardDataVersion = cardDataVersion;
        BehaviorRegistryVersion = behaviorRegistryVersion;
        Seed = seed;
        InitialSnapshot = initialSnapshot;
        Actions = new ReadOnlyCollection<ReplayAction>(actions.Select(CloneAction).ToArray());
        ExpectedFinalStateHash = expectedFinalStateHash;
        BehaviorCatalogFingerprint = behaviorCatalogFingerprint;
    }

    public ReplayArtifact(
        GameSnapshot initialSnapshot,
        ulong seed,
        IReadOnlyList<ReplayAction>? actions = null,
        string? rulesVersion = null,
        string? cardDataVersion = null,
        string? behaviorRegistryVersion = null,
        string? expectedFinalStateHash = null,
        string? behaviorCatalogFingerprint = null)
        : this(
            EngineCompatibility.ArtifactSchemaVersion,
            EngineArtifactKinds.Replay,
            rulesVersion ?? initialSnapshot.RulesVersion,
            cardDataVersion ?? initialSnapshot.CardDataVersion,
            behaviorRegistryVersion ?? initialSnapshot.BehaviorRegistryVersion,
            seed,
            initialSnapshot,
            actions ?? [],
            expectedFinalStateHash,
            behaviorCatalogFingerprint ?? initialSnapshot.BehaviorCatalogFingerprint)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int SchemaVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string ArtifactKind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string RulesVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CardDataVersion { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string BehaviorRegistryVersion { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BehaviorCatalogFingerprint { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ulong Seed { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public GameSnapshot InitialSnapshot { get; private set; }

    [JsonIgnore]
    public GameSnapshot InitialState => InitialSnapshot;

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<ReplayAction> Actions { get; private set; }

    public string? ExpectedFinalStateHash { get; }

    public IReadOnlyList<StateInvariantViolation> CollectViolations()
    {
        return CollectViolations(null);
    }

    public IReadOnlyList<StateInvariantViolation> CollectViolations(IBehaviorCatalog? behaviorCatalog)
    {
        var violations = new List<StateInvariantViolation>();
        if (SchemaVersion != EngineCompatibility.ArtifactSchemaVersion)
        {
            Add(
                violations,
                "artifact.unsupported-schema",
                "schemaVersion",
                $"Replay schema version {SchemaVersion} is not supported; expected {EngineCompatibility.ArtifactSchemaVersion}.");
        }

        if (!string.Equals(ArtifactKind, EngineArtifactKinds.Replay, StringComparison.Ordinal))
        {
            Add(violations, "artifact.kind-mismatch", "artifactKind", "The artifact kind must be 'replay'.");
        }

        if (!string.Equals(RulesVersion, EngineCompatibility.RulesVersion, StringComparison.Ordinal))
        {
            Add(
                violations,
                "artifact.unsupported-rules",
                "rulesVersion",
                $"Rules version '{RulesVersion}' is not supported; expected '{EngineCompatibility.RulesVersion}'.");
        }

        if (!string.Equals(CardDataVersion, EngineCompatibility.CardDataVersion, StringComparison.Ordinal)
            && !string.Equals(CardDataVersion, EngineCompatibility.GatewayCardDataVersion, StringComparison.Ordinal)
            // Read the pre-D-401 M2 artifact while requiring new artifacts to
            // carry the authenticated metadata commit above.
            && !string.Equals(CardDataVersion, "gateway-m2-3", StringComparison.Ordinal))
        {
            Add(
                violations,
                "artifact.unsupported-card-data",
                "cardDataVersion",
                $"Card-data version '{CardDataVersion}' is not supported; expected '{EngineCompatibility.CardDataVersion}' or '{EngineCompatibility.GatewayCardDataVersion}'.");
        }

        if (!string.Equals(BehaviorRegistryVersion, EngineCompatibility.BehaviorRegistryVersion, StringComparison.Ordinal)
            && !string.Equals(BehaviorRegistryVersion, EngineCompatibility.GatewayBehaviorRegistryVersion, StringComparison.Ordinal))
        {
            Add(
                violations,
                "artifact.unsupported-behaviors",
                "behaviorRegistryVersion",
                $"Behavior registry version '{BehaviorRegistryVersion}' is not supported; expected '{EngineCompatibility.BehaviorRegistryVersion}' or '{EngineCompatibility.GatewayBehaviorRegistryVersion}'.");
        }

        if (BehaviorCatalogFingerprint is not null
            && !DeterministicHash.IsSha256Hex(BehaviorCatalogFingerprint))
        {
            Add(
                violations,
                "artifact.invalid-behavior-fingerprint",
                "behaviorCatalogFingerprint",
                "Behavior catalog fingerprints must be lowercase SHA-256 hex.");
        }

        if (!string.Equals(BehaviorRegistryVersion, InitialSnapshot.BehaviorRegistryVersion, StringComparison.Ordinal))
        {
            Add(
                violations,
                "artifact.behavior-version-mismatch",
                "behaviorRegistryVersion",
                "Replay behavior registry version must match the initial snapshot behavior registry version.");
        }

        if (BehaviorCatalogFingerprint is not null
            && InitialSnapshot.BehaviorCatalogFingerprint is not null
            && !string.Equals(BehaviorCatalogFingerprint, InitialSnapshot.BehaviorCatalogFingerprint, StringComparison.Ordinal))
        {
            Add(
                violations,
                "artifact.behavior-fingerprint-mismatch",
                "behaviorCatalogFingerprint",
                "Replay behavior catalog fingerprint must match the initial snapshot fingerprint.");
        }

        try
        {
            StateInvariantValidator.Validate(InitialSnapshot.ToState(), behaviorCatalog);
        }
        catch (StateInvariantException exception)
        {
            foreach (StateInvariantViolation violation in exception.Violations)
            {
                Add(violations, $"artifact.initial.{violation.Code}", $"initialSnapshot.{violation.Path}", violation.Message);
            }
        }

        GameState initialState = InitialSnapshot.ToState();
        if (Seed != initialState.InitialSeed)
        {
            Add(
                violations,
                "artifact.seed-mismatch",
                "seed",
                $"Replay seed {Seed} does not match the initial snapshot seed {initialState.InitialSeed}.");
        }

        var playerIds = initialState.Players.Select(static player => player.Id).ToHashSet();
        long previousSequence = initialState.ActionSequence;
        foreach (ReplayAction? action in Actions)
        {
            if (action is null)
            {
                Add(violations, "artifact.null-action", "actions", "Replay actions must not contain null entries.");
                continue;
            }

            if (action.Sequence <= previousSequence)
            {
                Add(
                    violations,
                    "artifact.nonmonotonic-action-sequence",
                    "actions",
                    "Replay action sequences must be strictly increasing.");
            }
            else if (previousSequence == long.MaxValue || action.Sequence != previousSequence + 1)
            {
                Add(
                    violations,
                    "artifact.noncontiguous-action-sequence",
                    "actions",
                    $"Replay action sequence {action.Sequence} must immediately follow {previousSequence}.");
            }

            ActionSubmission submission = action.Submission;
            if (!StableIdentifier.TryValidate(submission.Actor.Value))
            {
                Add(violations, "artifact.invalid-actor", "actions.submission.actor", "Replay action actor ID is empty or invalid.");
            }
            else if (!playerIds.Contains(submission.Actor))
            {
                Add(
                    violations,
                    "artifact.unknown-actor",
                    "actions.submission.actor",
                    $"Replay action actor '{submission.Actor}' is not present in the initial snapshot.");
            }

            if (!StableIdentifier.TryValidate(submission.DecisionId.Value))
            {
                Add(violations, "artifact.invalid-decision", "actions.submission.decisionId", "Replay decision ID is empty or invalid.");
            }

            if (submission.Revision <= 0)
            {
                Add(violations, "artifact.invalid-revision", "actions.submission.revision", "Replay decision revisions must be positive.");
            }

            if (!StableIdentifier.TryValidate(submission.ActionId.Value))
            {
                Add(violations, "artifact.invalid-action", "actions.submission.actionId", "Replay action ID is empty or invalid.");
            }

            if (action.ExpectedStateHash is not null && !DeterministicHash.IsSha256Hex(action.ExpectedStateHash))
            {
                Add(violations, "artifact.invalid-state-hash", "actions.expectedStateHash", "Expected state hashes must be lowercase SHA-256 hex.");
            }

            if (action.ExpectedEventBatchHash is not null && !DeterministicHash.IsSha256Hex(action.ExpectedEventBatchHash))
            {
                Add(violations, "artifact.invalid-event-hash", "actions.expectedEventBatchHash", "Expected event hashes must be lowercase SHA-256 hex.");
            }

            previousSequence = action.Sequence;
        }

        if (ExpectedFinalStateHash is not null && !DeterministicHash.IsSha256Hex(ExpectedFinalStateHash))
        {
            Add(violations, "artifact.invalid-final-hash", "expectedFinalStateHash", "Expected final state hash must be lowercase SHA-256 hex.");
        }

        return violations.AsReadOnly();

        static void Add(List<StateInvariantViolation> target, string code, string path, string message) =>
            target.Add(new StateInvariantViolation(code, path, message));
    }

    public void Validate()
    {
        Validate(null);
    }

    public void Validate(IBehaviorCatalog? behaviorCatalog)
    {
        IReadOnlyList<StateInvariantViolation> violations = CollectViolations(behaviorCatalog);
        if (violations.Count > 0)
        {
            throw new StateInvariantException(violations);
        }
    }

    private static ReplayAction CloneAction(ReplayAction? action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new ReplayAction(
            action.Sequence,
            action.Submission,
            action.ExpectedStateHash,
            action.ExpectedEventBatchHash);
    }
}

public enum ReplayVerificationStatus
{
    Success,
    InvalidArtifact,
    Rejected,
    Diverged,
}

public sealed record ReplayVerificationResult
{
    private ReplayVerificationResult(
        ReplayVerificationStatus status,
        string message,
        long? actionSequence,
        string? expectedHash,
        string? actualHash,
        string? finalStateHash,
        GameState? finalState)
    {
        Status = status;
        Message = message;
        ActionSequence = actionSequence;
        ExpectedHash = expectedHash;
        ActualHash = actualHash;
        FinalStateHash = finalStateHash;
        FinalState = finalState;
    }

    public ReplayVerificationStatus Status { get; }

    public bool Success => Status == ReplayVerificationStatus.Success;

    public string Message { get; }

    public long? ActionSequence { get; }

    public string? ExpectedHash { get; }

    public string? ActualHash { get; }

    public string? FinalStateHash { get; }

    [JsonIgnore]
    public GameState? FinalState { get; }

    [JsonIgnore]
    public long? FailingActionSequence => ActionSequence;

    internal static ReplayVerificationResult Invalid(string message) =>
        new(ReplayVerificationStatus.InvalidArtifact, message, null, null, null, null, null);

    internal static ReplayVerificationResult Rejected(long sequence, string message) =>
        new(ReplayVerificationStatus.Rejected, message, sequence, null, null, null, null);

    internal static ReplayVerificationResult Diverged(
        long sequence,
        string message,
        string expected,
        string actual,
        GameState? finalState = null) =>
        new(ReplayVerificationStatus.Diverged, message, sequence, expected, actual, null, finalState);

    internal static ReplayVerificationResult Succeeded(GameState finalState, string finalStateHash) =>
        new(ReplayVerificationStatus.Success, "Replay verified.", null, null, null, finalStateHash, finalState);
}

public static class ReplayVerifier
{
    public static ReplayVerificationResult Verify(ReplayArtifact artifact)
    {
        return Verify(artifact, BehaviorCatalog.Empty, enforceCatalogCompatibility: false);
    }

    /// <summary>
    /// Verifies a replay against the explicitly composed immutable behavior
    /// catalog captured by a match/session.
    /// </summary>
    public static ReplayVerificationResult Verify(
        ReplayArtifact artifact,
        IBehaviorCatalog behaviorCatalog)
    {
        return Verify(artifact, behaviorCatalog, enforceCatalogCompatibility: true);
    }

    private static ReplayVerificationResult Verify(
        ReplayArtifact artifact,
        IBehaviorCatalog behaviorCatalog,
        bool enforceCatalogCompatibility)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        IReadOnlyList<StateInvariantViolation> violations = artifact.CollectViolations(
            enforceCatalogCompatibility ? behaviorCatalog : null);
        if (violations.Count > 0)
        {
            return ReplayVerificationResult.Invalid(string.Join("; ", violations.Select(static violation => violation.ToString())));
        }

        GameState current = artifact.InitialSnapshot.ToState();
        if (enforceCatalogCompatibility
            && (!string.Equals(current.BehaviorRegistryVersion, behaviorCatalog.Version, StringComparison.Ordinal)
                || current.BehaviorCatalogFingerprint is not null
                    && !string.Equals(current.BehaviorCatalogFingerprint, behaviorCatalog.Fingerprint, StringComparison.Ordinal)))
        {
            return ReplayVerificationResult.Invalid(
                $"Replay behavior catalog does not match the injected catalog '{behaviorCatalog.Version}/{behaviorCatalog.Fingerprint}'.");
        }

        if (artifact.Actions.Count > 0 && current.ActionSequence == long.MaxValue)
        {
            return ReplayVerificationResult.Invalid(
                "The initial state's action sequence is exhausted but the replay contains actions.");
        }

        long expectedSequence = current.ActionSequence + 1;
        foreach (ReplayAction action in artifact.Actions)
        {
            if (action.Sequence != expectedSequence)
            {
                return ReplayVerificationResult.Invalid(
                    $"Replay action sequence {action.Sequence} does not follow expected sequence {expectedSequence}.");
            }

            ApplyResult result = enforceCatalogCompatibility
                ? GameEngine.Apply(current, action.Submission, behaviorCatalog)
                : GameEngine.Apply(current, action.Submission);
            if (!result.Accepted)
            {
                return ReplayVerificationResult.Rejected(
                    action.Sequence,
                    $"Action sequence {action.Sequence} was rejected: {result.Rejection?.Code}: {result.Rejection?.Message}");
            }

            AcceptedActionRecord accepted = result.AcceptedAction
                ?? throw new InvalidOperationException("An accepted ApplyResult must contain an action record.");
            if (accepted.Sequence != action.Sequence || accepted.Submission != action.Submission)
            {
                return ReplayVerificationResult.Invalid(
                    $"Accepted action record at sequence {action.Sequence} does not match its replay submission.");
            }

            if (action.ExpectedStateHash is not null
                && !string.Equals(action.ExpectedStateHash, accepted.ResultingStateHash, StringComparison.Ordinal))
            {
                return ReplayVerificationResult.Diverged(
                    action.Sequence,
                    $"State hash diverged at action sequence {action.Sequence}.",
                    action.ExpectedStateHash,
                    accepted.ResultingStateHash,
                    result.State);
            }

            if (action.ExpectedEventBatchHash is not null
                && !string.Equals(action.ExpectedEventBatchHash, accepted.ResultingEventBatchHash, StringComparison.Ordinal))
            {
                return ReplayVerificationResult.Diverged(
                    action.Sequence,
                    $"Event-batch hash diverged at action sequence {action.Sequence}.",
                    action.ExpectedEventBatchHash,
                    accepted.ResultingEventBatchHash,
                    result.State);
            }

            current = result.State;
            if (expectedSequence == long.MaxValue)
            {
                if (action != artifact.Actions[^1])
                {
                    return ReplayVerificationResult.Invalid(
                        $"Replay action sequence {action.Sequence} exhausted the action sequence counter before the replay ended.");
                }
            }
            else
            {
                expectedSequence++;
            }
        }

        string finalHash = DeterministicHash.ForState(current);
        if (artifact.ExpectedFinalStateHash is not null
            && !string.Equals(artifact.ExpectedFinalStateHash, finalHash, StringComparison.Ordinal))
        {
            long sequence = current.ActionSequence;
            return ReplayVerificationResult.Diverged(
                sequence,
                $"Final state hash diverged after action sequence {sequence}.",
                artifact.ExpectedFinalStateHash,
                finalHash,
                current);
        }

        return ReplayVerificationResult.Succeeded(current, finalHash);
    }
}
