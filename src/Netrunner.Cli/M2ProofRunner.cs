using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.Cli;

/// <summary>Raised when the M2 proof cannot complete the curated run.</summary>
public sealed class M2ProofException : InvalidOperationException
{
    public M2ProofException(string message)
        : base(message)
    {
    }

    public M2ProofException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// One save/load checkpoint taken at a yielded decision: the decision ID and
/// the authoritative state hash that must be reproduced by the canonical
/// save/load round trip.
/// </summary>
public sealed record M2DecisionCheckpoint(string DecisionId, string StateHash);

/// <summary>
/// The full M2 milestone-proof evidence for one policy driving the curated
/// run: the initial and final states, the frozen event stream, the replay
/// artifact and its verification, the cited timing trace, and the
/// save/load-at-every-decision checkpoints.
/// </summary>
public sealed class M2ProofResult
{
    internal M2ProofResult(
        string policyName,
        GameState initialState,
        string initialStateHash,
        GameState finalState,
        string finalStateHash,
        IReadOnlyList<EngineEvent> frozenEvents,
        string eventStreamHash,
        IReadOnlyList<ReplayAction> replayActions,
        GameSnapshot replaySnapshot,
        ReplayArtifact replayArtifact,
        RunTimingTrace trace,
        IReadOnlyList<M2DecisionCheckpoint> checkpoints)
    {
        PolicyName = policyName;
        InitialState = initialState;
        InitialStateHash = initialStateHash;
        FinalState = finalState;
        FinalStateHash = finalStateHash;
        FrozenEvents = frozenEvents;
        EventStreamHash = eventStreamHash;
        ReplayActions = replayActions;
        ReplaySnapshot = replaySnapshot;
        ReplayArtifact = replayArtifact;
        Trace = trace;
        Checkpoints = checkpoints;
    }

    public string PolicyName { get; }

    /// <summary>The built scenario state (the hash pinned by the scenario document).</summary>
    public GameState InitialState { get; }

    public string InitialStateHash { get; }

    public GameState FinalState { get; }

    public string FinalStateHash { get; }

    public IReadOnlyList<EngineEvent> FrozenEvents { get; }

    public string EventStreamHash { get; }

    public IReadOnlyList<ReplayAction> ReplayActions { get; }

    /// <summary>
    /// The state right after the contracted run start: the replay artifact
    /// starts here, because the recorded actions are the run-window decisions.
    /// </summary>
    public GameSnapshot ReplaySnapshot { get; }

    public ReplayArtifact ReplayArtifact { get; }

    public RunTimingTrace Trace { get; }

    public IReadOnlyList<M2DecisionCheckpoint> Checkpoints { get; }
}

/// <summary>
/// Offline coordinator for the M2 milestone proof. It builds the curated
/// scenario, opens the contracted run, and drives it to completion through
/// the filtered observation boundary only: every yielded decision round-trips
/// through the canonical save/load format and the loop RESUMES from the
/// deserialized state (docs/history/M2_ARCHITECTURE.md:170 — the live object never
/// drives the decision, the observation, or the apply of an iteration), the
/// policy selects only issued legal action IDs, and the result is
/// re-verified by the replay artifact and the run-timing trace validator.
/// </summary>
public static class M2ProofRunner
{
    public const int MaxAcceptedActions = 256;

    public static M2ProofResult Run(
        M2ScenarioDocument document,
        IGameAgent agent,
        string policyName) =>
        Run(document, agent, policyName, behaviorCatalog: null);

    /// <summary>
    /// Runs the proof against an injected composition catalog. The proof uses
    /// the immutable scenario slice derived from that catalog.
    /// </summary>
    public static M2ProofResult Run(
        M2ScenarioDocument document,
        IGameAgent agent,
        string policyName,
        IBehaviorCatalog? behaviorCatalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(policyName);

        IBehaviorCatalog suppliedCatalog = behaviorCatalog ?? GatewayBehaviorCatalog.Create();
        BehaviorCatalog scenarioCatalog = document.Validate(suppliedCatalog);
        return RunWithScenarioCatalog(document, agent, policyName, scenarioCatalog);
    }

    /// <summary>
    /// Runs the proof against an already validated immutable scenario slice.
    /// The command path uses this seam to share one slice across both policy
    /// runs and its final replay verification.
    /// </summary>
    internal static M2ProofResult RunWithScenarioCatalog(
        M2ScenarioDocument document,
        IGameAgent agent,
        string policyName,
        BehaviorCatalog scenarioCatalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(policyName);
        ArgumentNullException.ThrowIfNull(scenarioCatalog);

        M2ScenarioRunSpec runSpec = document.Run
            ?? throw new M2ProofException($"scenario '{document.ScenarioId}': the scenario has no run contract.");

        GameState initial = M2ScenarioSetup.Build(document, scenarioCatalog);
        string initialStateHash = DeterministicHash.ForState(initial);

        ServerId serverId = ResolveServerId(initial, runSpec.Server);
        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(
            initial,
            serverId,
            scenarioCatalog,
            initiatingSource: new CardInstanceId(runSpec.Source));
        GameState current = start.State;

        // The replay artifact starts at the run-start state: the recorded
        // actions are the run-window decisions, and the run start itself is
        // pinned by the scenario document's run contract.
        GameSnapshot replaySnapshot = current.CreateSnapshot(scenarioCatalog);

        var events = new List<EngineEvent>(start.Events);
        var replayActions = new List<ReplayAction>();
        var checkpoints = new List<M2DecisionCheckpoint>();

        for (int index = 0; index < MaxAcceptedActions; index++)
        {
            if (current.RunState is null && current.PendingDecision is null)
            {
                break;
            }

            PendingDecision pending = current.PendingDecision
                ?? throw new M2ProofException($"the curated run stalled at step {index}: no pending decision was present.");

            // Save/load at every yielded decision: the canonical round trip
            // must reproduce the authoritative state byte-for-byte, and the
            // iteration then resumes from the deserialized state, so the
            // proof exercises resumption, not only round-trip equality
            // (docs/history/M2_ARCHITECTURE.md:170).
            string saved = EngineJson.Serialize(current);
            GameState restored = EngineJson.DeserializeState(saved, scenarioCatalog);
            string liveHash = DeterministicHash.ForState(current);
            if (!string.Equals(DeterministicHash.ForState(restored), liveHash, StringComparison.Ordinal))
            {
                throw new M2ProofException(
                    $"save/load diverged at decision '{pending.Id}': the live state hashes to {liveHash} "
                    + "but the restored state does not match it.");
            }

            // The round trip is byte-equal, so the restored pending decision
            // must be the one just recorded; re-read it and continue from
            // here — the live object is not used for this iteration anymore.
            PendingDecision resumed = restored.PendingDecision
                ?? throw new M2ProofException($"the restored state lost its pending decision at '{pending.Id}'.");
            if (resumed.Id != pending.Id || resumed.Revision != pending.Revision || resumed.Owner != pending.Owner)
            {
                throw new M2ProofException(
                    $"the restored decision (id '{resumed.Id}', revision {resumed.Revision}, owner '{resumed.Owner}') "
                    + $"does not match the live decision (id '{pending.Id}', revision {pending.Revision}, owner '{pending.Owner}'); "
                    + "the round trip cannot resume.");
            }

            checkpoints.Add(new M2DecisionCheckpoint(resumed.Id.Value, liveHash));

            PlayerSide viewer = restored.GetPlayer(resumed.Owner).Side;
            Observation observation = ObservationBuilder.Build(restored, viewer, scenarioCatalog);

            ActionId selected;
            try
            {
                selected = agent
                    .ChooseActionAsync(observation)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
            {
                throw new M2ProofException(
                    $"the {policyName} could not choose an action at decision '{pending.Id}': {exception.Message}",
                    exception);
            }

            if (resumed.LegalActions.FirstOrDefault(action => action.Id == selected) is null)
            {
                throw new M2ProofException(
                    $"the {policyName} selected unoffered action '{selected}' for decision '{resumed.Id}'.");
            }

            var submission = new ActionSubmission(resumed.Owner, resumed.Id, resumed.Revision, selected);
            ApplyResult applied = GameEngine.Apply(restored, submission, scenarioCatalog);
            if (!applied.Accepted)
            {
                throw new M2ProofException(
                    $"the {policyName} action was rejected at decision '{pending.Id}': "
                    + $"{applied.Rejection?.Code}: {applied.Rejection?.Message}");
            }

            if (applied.AcceptedAction is null)
            {
                throw new M2ProofException("An accepted action did not produce an action record.");
            }

            replayActions.Add(ReplayAction.From(applied.AcceptedAction));
            events.AddRange(applied.Events);
            current = applied.State;
        }

        if (current.RunState is not null || current.PendingDecision is not null)
        {
            throw new M2ProofException(
                $"the curated run did not complete within {MaxAcceptedActions} accepted actions.");
        }

        string finalStateHash = DeterministicHash.ForState(current);
        IReadOnlyList<EngineEvent> frozenEvents = EngineEventValidator.FreezeBatch(events);
        string eventStreamHash = DeterministicHash.ForEvents(frozenEvents);
        var replay = new ReplayArtifact(
            replaySnapshot,
            document.Seed,
            replayActions,
            behaviorRegistryVersion: replaySnapshot.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: replaySnapshot.BehaviorCatalogFingerprint,
            expectedFinalStateHash: finalStateHash);
        ReplayVerificationResult verification = ReplayVerifier.Verify(replay, scenarioCatalog);
        if (!verification.Success
            || !string.Equals(verification.FinalStateHash, finalStateHash, StringComparison.Ordinal))
        {
            throw new M2ProofException($"the curated run replay failed verification: {verification.Message}");
        }

        RunTimingTrace trace = RunTimingTraceValidator.Validate(frozenEvents);
        if (!trace.IsValid)
        {
            throw new M2ProofException(
                $"the run-timing trace found {trace.Problems.Count} problem(s): {string.Join("; ", trace.Problems)}");
        }

        return new M2ProofResult(
            policyName,
            initial,
            initialStateHash,
            current,
            finalStateHash,
            frozenEvents,
            eventStreamHash,
            replayActions,
            replaySnapshot,
            replay,
            trace,
            checkpoints);
    }

    private static ServerId ResolveServerId(GameState state, string reference)
    {
        if (!M2ScenarioDocument.TryParseServerRef(reference, state.Servers.Count - 3, out var parsed))
        {
            throw new M2ProofException($"scenario run server '{reference}' is not a valid server reference.");
        }

        ServerState? server = parsed.Kind == ServerKind.Remote
            ? state.Servers.ElementAt(3 + parsed.RemoteIndex)
            : state.Servers.SingleOrDefault(candidate => candidate.Kind == parsed.Kind);
        return server?.Id
            ?? throw new M2ProofException($"scenario run server '{reference}' was not found in the built state.");
    }
}
