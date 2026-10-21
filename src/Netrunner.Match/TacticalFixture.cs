using System.Collections.Immutable;
using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

public sealed record TacticalCaseMetadata(string Id, PlayerSide Side, string Category, string Split, bool Critical,
    string Title, string Preconditions, string Acceptance);

internal sealed record TacticalStep(PlayerSide Side, DecisionKind Decision, LegalAction Action);

internal sealed class TacticalTrace(TacticalFixture fixture)
{
    public TacticalFixture Fixture { get; } = fixture;
    public GameState State { get; set; } = fixture.Initial;
    public List<TacticalStep> Steps { get; } = [];
    public List<ReplayAction> ReplayActions { get; } = [];
    public AgentKnowledge Knowledge { get; set; } = AgentKnowledge.Empty;
    public OfflineAgentCheckpoint? PolicyCheckpoint { get; set; }
    public IEnumerable<TacticalStep> OwnSteps => Steps.Where(step => step.Side == Fixture.Metadata.Side);
    public PlayerState Player(PlayerSide side) => State.Players.Single(player => player.Side == side);
}

internal sealed record TacticalFixture(TacticalCaseMetadata Metadata, TacticalBoard Board, GameState Initial,
    Func<TacticalTrace, LegalAction> Oracle, Func<TacticalTrace, bool> Complete, Func<TacticalTrace, bool> Success,
    int ActionCap = 80, Func<GameState, LegalAction>? Opponent = null, GameState? HiddenTwin = null,
    Func<TacticalTrace, LegalAction?>? Counterexample = null, bool ForcedContinuation = false)
{
    public static TacticalFixture FirstChoice(TacticalCaseMetadata metadata, TacticalBoard board, GameState state,
        Func<LegalAction, bool> acceptable, bool requireAlternative = true)
    {
        PendingDecision pending = state.PendingDecision ?? throw new InvalidOperationException($"{metadata.Id}: no issued decision.");
        if (state.GetPlayer(pending.Owner).Side != metadata.Side || !pending.LegalActions.Any(acceptable)
            || requireAlternative && pending.LegalActions.All(acceptable))
            throw new InvalidOperationException($"{metadata.Id}: the declared tactic or its contrasting choice was not materialized.");
        return new(metadata, board, state,
            trace => trace.State.PendingDecision!.LegalActions.First(acceptable),
            trace => trace.OwnSteps.Any(),
            trace => trace.OwnSteps.FirstOrDefault() is { } first && acceptable(first.Action),
            Counterexample: requireAlternative ? trace => trace.State.PendingDecision!.LegalActions.First(action => !acceptable(action)) : null);
    }
}

public sealed record TacticalCaseResult(string Id, PlayerSide Side, string Category, string Split, bool Critical,
    string InitialStateHash, bool Passed, bool Completed, int Actions, bool ReplayVerified, string ReplayHash,
    string? Error, string ReplayPath, ImmutableArray<string> SemanticActions, ImmutableArray<string> ObservationDigests,
    ImmutableArray<string> SelectedActionIds, ImmutableArray<string> EvaluationDigests, string? CheckpointDigest,
    bool? HiddenTwinEquivalent = null, string? HiddenTwinReplayHash = null,
    bool? HiddenTwinReplayVerified = null, string? HiddenTwinError = null);

internal sealed record TacticalArtifacts(TacticalCaseResult Result, ReplayArtifact Replay)
{
    public ReplayArtifact? HiddenTwinReplay { get; init; }
    public TacticalArtifacts? HiddenTwinArtifacts { get; init; }
    public GameSnapshot? FinalSnapshot { get; init; }
    public ImmutableArray<TacticalStep> RecentSteps { get; init; } = [];
    public ImmutableArray<AgentEvaluation> Evaluations { get; init; } = [];
    public OfflineAgentCheckpoint? Checkpoint { get; init; }
    public bool CounterexampleApplied { get; init; }
}

/// <summary>
/// A bounded harness for prepared Core positions. Policy input remains the
/// filtered observation; oracle scripts and outcome predicates stay here.
/// </summary>
internal static class TacticalExecution
{
    public static async Task<TacticalArtifacts> RunAsync(TacticalFixture fixture,
        IGameAgent? agent, bool oracle = false, bool rejectFirstOracleChoice = false, Func<IGameAgent>? twinFactory = null,
        CancellationToken cancellationToken = default)
    {
        if (!oracle && agent is null) throw new ArgumentNullException(nameof(agent));
        if (!oracle && fixture.HiddenTwin is not null && twinFactory is null)
            throw new ArgumentException("An information-boundary tactic requires an independent identical-policy twin factory.", nameof(twinFactory));
        var trace = new TacticalTrace(fixture);
        var observations = ImmutableArray.CreateBuilder<string>();
        var evaluations = ImmutableArray.CreateBuilder<string>();
        var evaluationRecords = ImmutableArray.CreateBuilder<AgentEvaluation>();
        TacticalBoard board = fixture.Board;
        StateInvariantValidator.Validate(trace.State, board.Behavior, board.Runtime);
        GameSnapshot initial = trace.State.CreateSnapshot(board.Behavior, board.Runtime);
        string? error = null;
        bool completed = false;
        bool counterexampleApplied = false;
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, watchdog.Token);
        try
        {
            RecordObservation();
            // A prepared position starts a fresh policy observation history at
            // cursor zero. Replay sequences retain their actual engine values.
            if (agent is IAgentLifecycle lifecycle)
            {
                AgentLifecycleHost.ValidateSeat(agent, fixture.Metadata.Side);
                Observation observation = ObservationBuilder.Build(trace.State, fixture.Metadata.Side, board.Behavior);
                await Callback(() => lifecycle.Observe(observation, 0)).ConfigureAwait(false);
                if (agent is OfflineAgent initialPolicy) trace.PolicyCheckpoint = initialPolicy.Checkpoint;
            }
            while (!fixture.Complete(trace) && trace.Steps.Count < fixture.ActionCap && trace.State.Outcome.Kind == GameOutcomeKind.Ongoing)
            {
                linked.Token.ThrowIfCancellationRequested();
                PendingDecision pending = trace.State.PendingDecision ?? throw new InvalidOperationException("Tactical position stalled without a decision.");
                PlayerSide side = trace.State.GetPlayer(pending.Owner).Side;
                LegalAction action;
                if (side != fixture.Metadata.Side) action = fixture.Opponent?.Invoke(trace.State) ?? TacticalBoard.Passive(trace.State);
                else if (oracle)
                {
                    action = fixture.Oracle(trace);
                    if (rejectFirstOracleChoice && !counterexampleApplied)
                    {
                        LegalAction? alternative = fixture.Counterexample is { } counterexample ? counterexample(trace)
                            : pending.LegalActions.FirstOrDefault(candidate => candidate.Id != action.Id);
                        if (alternative is not null) { action = alternative; counterexampleApplied = true; }
                    }
                }
                else
                {
                    Observation observation = ObservationBuilder.Build(trace.State, side, board.Behavior, board.Runtime);
                    Task<ActionId> choose = Task.Run(async () => await agent!.ChooseActionAsync(observation, linked.Token).ConfigureAwait(false), CancellationToken.None);
                    ActionId id = await Await(choose).ConfigureAwait(false);
                    action = pending.LegalActions.SingleOrDefault(candidate => candidate.Id == id)
                        ?? throw new InvalidOperationException("Agent returned an action not issued for the tactical decision.");
                }
                linked.Token.ThrowIfCancellationRequested();
                if (side == fixture.Metadata.Side && agent is OfflineAgent { LastEvaluation: { } evaluation })
                {
                    evaluations.Add(BenchmarkHash.Value(evaluation));
                    evaluationRecords.Add(evaluation);
                }
                ApplyResult applied = board.Apply(trace.State, action);
                trace.Steps.Add(new(side, pending.Kind, action));
                trace.ReplayActions.Add(ReplayAction.From(applied.AcceptedAction!));
                trace.State = applied.State;
                RecordObservation();
                if (agent is IAgentLifecycle active)
                {
                    int cursor = trace.Steps.Count;
                    Observation observation = ObservationBuilder.Build(trace.State, fixture.Metadata.Side, board.Behavior);
                    await Callback(() =>
                    {
                        if (side == fixture.Metadata.Side) active.CommitDecision(pending.Id, pending.Revision, action.Id, cursor);
                        active.Observe(observation, cursor);
                    }).ConfigureAwait(false);
                    if (agent is OfflineAgent offline) trace.PolicyCheckpoint = offline.Checkpoint;
                }
            }
            completed = fixture.Complete(trace);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
        }
        var replay = new ReplayArtifact(initial, trace.State.InitialSeed, trace.ReplayActions, expectedFinalStateHash: DeterministicHash.ForState(trace.State));
        bool verified = false;
        bool passed = false;
        try
        {
            ReplayVerificationResult verification = await Task.Run(() => ReplayVerifier.Verify(replay, board.Behavior, board.Runtime), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
            verified = verification.Success;
            if (!verified) error = (error ?? "") + "; replay: " + verification.Message;
            passed = error is null && completed && verified && fixture.Success(trace);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            error = (error ?? "") + $"; verification: {exception.GetType().Name}: {exception.Message}";
        }
        var result = new TacticalCaseResult(fixture.Metadata.Id, fixture.Metadata.Side, fixture.Metadata.Category, fixture.Metadata.Split, fixture.Metadata.Critical,
            DeterministicHash.ForState(fixture.Initial), passed, completed, trace.Steps.Count, verified, BenchmarkHash.Value(replay),
            error, fixture.Metadata.Id + "/replay.json",
            trace.OwnSteps.Select(step => step.Action.Kind + ":" + step.Action.Parameters.GetType().Name).ToImmutableArray(), observations.ToImmutable(),
            trace.OwnSteps.Select(step => step.Action.Id.Value).ToImmutableArray(), evaluations.ToImmutable(),
            trace.PolicyCheckpoint is { } checkpoint ? BenchmarkHash.Value(checkpoint) : null);
        ReplayArtifact? twinReplay = null;
        TacticalArtifacts? twinArtifacts = null;
        if (fixture.HiddenTwin is { } twinState)
        {
            IGameAgent? twinAgent = oracle ? null : twinFactory!();
            if (!oracle && ReferenceEquals(agent, twinAgent)) throw new ArgumentException("Hidden-state twins require independent agent instances.", nameof(twinFactory));
            TacticalArtifacts twin = await RunAsync(fixture with { Initial = twinState, HiddenTwin = null }, twinAgent,
                oracle: oracle, cancellationToken: cancellationToken).ConfigureAwait(false);
            bool equivalent = result.ObservationDigests.SequenceEqual(twin.Result.ObservationDigests)
                && result.SelectedActionIds.SequenceEqual(twin.Result.SelectedActionIds)
                && result.EvaluationDigests.SequenceEqual(twin.Result.EvaluationDigests)
                && result.CheckpointDigest == twin.Result.CheckpointDigest;
            result = result with
            {
                Passed = result.Passed && twin.Result.Passed && equivalent,
                HiddenTwinEquivalent = equivalent,
                HiddenTwinReplayHash = twin.Result.ReplayHash,
                HiddenTwinReplayVerified = twin.Result.ReplayVerified,
                HiddenTwinError = twin.Result.Error,
            };
            twinReplay = twin.Replay;
            twinArtifacts = twin;
        }
        return new(result, replay)
        {
            HiddenTwinReplay = twinReplay,
            HiddenTwinArtifacts = twinArtifacts,
            FinalSnapshot = trace.State.CreateSnapshot(board.Behavior, board.Runtime),
            RecentSteps = trace.Steps.TakeLast(50).ToImmutableArray(),
            Evaluations = evaluationRecords.ToImmutable(),
            Checkpoint = trace.PolicyCheckpoint,
            CounterexampleApplied = counterexampleApplied,
        };

        void RecordObservation()
        {
            AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(trace.State, fixture.Metadata.Side, board.Behavior));
            observations.Add(ObservationReader.Digest(features));
            trace.Knowledge = trace.Knowledge.Observe(features, trace.Steps.Count);
        }

        async Task Callback(Action callback)
        {
            _ = await Await(Task.Run(() => { callback(); return true; }, CancellationToken.None)).ConfigureAwait(false);
        }

        async Task<T> Await<T>(Task<T> task)
        {
            try { return await task.WaitAsync(linked.Token).ConfigureAwait(false); }
            catch
            {
                _ = task.ContinueWith(static completedTask => { _ = completedTask.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw;
            }
        }
    }
}
