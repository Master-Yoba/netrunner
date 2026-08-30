using System.Collections.ObjectModel;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Cli;

/// <summary>
/// Result of one deterministic, headless M1 fixture match. The runner keeps
/// all replay inputs and typed event evidence so callers can persist or verify
/// the match without exposing authoritative state to an agent.
/// </summary>
public sealed class ToyMatchResult
{
    internal ToyMatchResult(
        ulong seed,
        ToyGoal goal,
        GameSnapshot initialSnapshot,
        GameState finalState,
        IReadOnlyList<AcceptedActionRecord> acceptedActions,
        IReadOnlyList<ReplayAction> replayActions,
        IReadOnlyList<EngineEvent> events,
        string finalStateHash,
        string eventStreamHash,
        ReplayArtifact replay)
    {
        ArgumentNullException.ThrowIfNull(initialSnapshot);
        ArgumentNullException.ThrowIfNull(finalState);
        ArgumentNullException.ThrowIfNull(acceptedActions);
        ArgumentNullException.ThrowIfNull(replayActions);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(finalStateHash);
        ArgumentNullException.ThrowIfNull(eventStreamHash);
        ArgumentNullException.ThrowIfNull(replay);

        Seed = seed;
        Goal = goal;
        InitialSnapshot = initialSnapshot;
        FinalState = finalState;
        AcceptedActions = new ReadOnlyCollection<AcceptedActionRecord>(acceptedActions.Select(CloneAcceptedAction).ToArray());
        ReplayActions = new ReadOnlyCollection<ReplayAction>(replayActions.Select(CloneReplayAction).ToArray());
        Events = EngineEventValidator.FreezeBatch(events);
        FinalStateHash = finalStateHash;
        EventStreamHash = eventStreamHash;
        Replay = replay;
    }

    public ulong Seed { get; }

    public ToyGoal Goal { get; }

    public GameSnapshot InitialSnapshot { get; }

    public GameState FinalState { get; }

    public IReadOnlyList<AcceptedActionRecord> AcceptedActions { get; }

    public IReadOnlyList<ReplayAction> ReplayActions { get; }

    public IReadOnlyList<EngineEvent> Events { get; }

    public int ActionCount => AcceptedActions.Count;

    public string FinalStateHash { get; }

    public string EventStreamHash { get; }

    public PlayerSide? Winner => FinalState.Outcome.Winner;

    public GameWinReason? Reason => FinalState.Outcome.Reason;

    public ReplayArtifact Replay { get; }

    private static AcceptedActionRecord CloneAcceptedAction(AcceptedActionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new AcceptedActionRecord(
            record.Sequence,
            new ActionSubmission(
                record.Submission.Actor,
                record.Submission.DecisionId,
                record.Submission.Revision,
                record.Submission.ActionId),
            record.ResultingStateHash,
            record.ResultingEventBatchHash);
    }

    private static ReplayAction CloneReplayAction(ReplayAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new ReplayAction(
            action.Sequence,
            new ActionSubmission(
                action.Submission.Actor,
                action.Submission.DecisionId,
                action.Submission.Revision,
                action.Submission.ActionId),
            action.ExpectedStateHash,
            action.ExpectedEventBatchHash);
    }
}

/// <summary>Raised when the deterministic coordinator cannot complete a toy match.</summary>
public sealed class ToyMatchException : InvalidOperationException
{
    public ToyMatchException(string message)
        : base(message)
    {
    }

    public ToyMatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Offline coordinator for the M1 proof. Core remains independent of Cards and
/// AI; this composition layer is the only place that joins fixture recipes to
/// the contract agent.
/// </summary>
public static class ToyMatchRunner
{
    public const int MaxAcceptedActions = 256;

    public static ToyMatchResult Run(ulong seed, ToyGoal goal)
    {
        var agent = new ToyGoalAgent(goal);
        GameState current = ToyGameSetup.Create(
            seed,
            FixtureDeckCatalog.CorpRecipe,
            FixtureDeckCatalog.RunnerRecipe);
        GameSnapshot initialSnapshot = current.CreateSnapshot();
        var acceptedActions = new List<AcceptedActionRecord>();
        var replayActions = new List<ReplayAction>();
        var events = new List<EngineEvent>();

        for (int index = 0; index < MaxAcceptedActions; index++)
        {
            if (current.Outcome.Kind == GameOutcomeKind.Won)
            {
                break;
            }

            PendingDecision pending = current.PendingDecision
                ?? throw new ToyMatchException(
                    $"The toy match stalled after {acceptedActions.Count} accepted actions: no pending decision was present.");
            // The observation builder is the sole production path from
            // authoritative state to the agent DTO; the toy agents consume the
            // same boundary as the M2 coordinators.
            PlayerSide viewer = current.GetPlayer(pending.Owner).Side;
            Observation observation = ObservationBuilder.Build(current, viewer);

            ActionId selectedAction;
            try
            {
                selectedAction = agent
                    .ChooseActionAsync(observation)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
            {
                throw new ToyMatchException(
                    $"The toy agent could not choose an action after {acceptedActions.Count} accepted actions: {exception.Message}",
                    exception);
            }

            if (!pending.LegalActions.Any(action => action.Id == selectedAction))
            {
                throw new ToyMatchException(
                    $"The toy agent selected unoffered action '{selectedAction}' for decision '{pending.Id}'.");
            }

            var submission = new ActionSubmission(
                pending.Owner,
                pending.Id,
                pending.Revision,
                selectedAction);
            ApplyResult applied = GameEngine.Apply(current, submission);
            if (!applied.Accepted)
            {
                throw new ToyMatchException(
                    $"The toy action was rejected at action {acceptedActions.Count + 1}: "
                    + $"{applied.Rejection?.Code}: {applied.Rejection?.Message}");
            }

            AcceptedActionRecord accepted = applied.AcceptedAction
                ?? throw new ToyMatchException("An accepted toy action did not produce an action record.");
            if (accepted.Sequence != acceptedActions.Count + 1
                || accepted.Submission != submission)
            {
                throw new ToyMatchException("The engine returned an inconsistent accepted action record.");
            }

            acceptedActions.Add(accepted);
            replayActions.Add(ReplayAction.From(accepted));
            events.AddRange(applied.Events);
            current = applied.State;
        }

        if (current.Outcome.Kind != GameOutcomeKind.Won)
        {
            throw new ToyMatchException(
                $"The toy match exceeded the {MaxAcceptedActions}-action safety cap without reaching a terminal outcome.");
        }

        string finalStateHash = DeterministicHash.ForState(current);
        IReadOnlyList<EngineEvent> frozenEvents = EngineEventValidator.FreezeBatch(events);
        string eventStreamHash = DeterministicHash.ForEvents(frozenEvents);
        var replay = new ReplayArtifact(
            initialSnapshot,
            seed,
            replayActions,
            expectedFinalStateHash: finalStateHash);
        ReplayVerificationResult verification = ReplayVerifier.Verify(replay);
        if (!verification.Success
            || !string.Equals(verification.FinalStateHash, finalStateHash, StringComparison.Ordinal))
        {
            throw new ToyMatchException(
                $"The generated toy replay failed immediate verification: {verification.Message}");
        }

        return new ToyMatchResult(
            seed,
            goal,
            initialSnapshot,
            current,
            acceptedActions,
            replayActions,
            frozenEvents,
            finalStateHash,
            eventStreamHash,
            replay);
    }

    public static ToyMatchResult RunMatch(ulong seed, ToyGoal goal) => Run(seed, goal);
}
