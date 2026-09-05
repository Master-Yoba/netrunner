using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

public sealed record FullMatchResult(GameOutcome Outcome, int ActionCount, string FinalStateHash, ReplayArtifact Replay);

/// <summary>Bounded observation-only agent driver over the same setup and reducer as the client.</summary>
public static class FullMatchRunner
{
    public static async Task<FullMatchResult> RunAsync(FullMatchDefinition definition, int actionCap = 1000,
        IGameAgent? corpAgent = null, IGameAgent? runnerAgent = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (actionCap <= 0 || actionCap > SimulationFixtureDocument.MaximumActionCap)
        {
            throw new ArgumentOutOfRangeException(nameof(actionCap));
        }
        corpAgent ??= new FullGameContractAgent(new FullGameContractPolicy(PlayerSide.Corp), definition.Runtime);
        runnerAgent ??= new FullGameContractAgent(new FullGameContractPolicy(PlayerSide.Runner), definition.Runtime);
        GameState state = definition.Start().State;
        GameSnapshot initial = state.CreateSnapshot(definition.Behavior, definition.Runtime);
        var actions = new List<ReplayAction>();
        while (state.Outcome.Kind == GameOutcomeKind.Ongoing && actions.Count < actionCap)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PendingDecision pending = state.PendingDecision ?? throw new InvalidOperationException("Full match stalled without a decision.");
            PlayerSide side = state.GetPlayer(pending.Owner).Side;
            Observation observation = ObservationBuilder.Build(state, side, definition.Behavior);
            ActionId action = await (side == PlayerSide.Corp ? corpAgent : runnerAgent).ChooseActionAsync(observation, cancellationToken).ConfigureAwait(false);
            // Every yield is a persistence boundary, including setup, nested effects,
            // paid windows, and forced discards. Restore with both frozen catalogs.
            state = EngineJson.DeserializeState(EngineJson.Serialize(state), definition.Behavior, definition.Runtime);
            ApplyResult result = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action), definition.Behavior, definition.Runtime);
            if (!result.Accepted)
            {
                throw new InvalidOperationException($"Full match rejected an agent action: {result.Rejection?.Message}");
            }
            state = result.State;
            actions.Add(ReplayAction.From(result.AcceptedAction!));
        }
        if (state.Outcome.Kind != GameOutcomeKind.Won)
        {
            throw new InvalidOperationException($"Full match exceeded the {actionCap}-action cap.");
        }
        string finalHash = DeterministicHash.ForState(state);
        ReplayArtifact replay = new(initial, state.InitialSeed, actions, expectedFinalStateHash: finalHash);
        replay = EngineJson.Deserialize<ReplayArtifact>(EngineJson.Serialize(replay), definition.Behavior, definition.Runtime);
        ReplayVerificationResult verified = ReplayVerifier.Verify(replay, definition.Behavior, definition.Runtime);
        if (!verified.Success)
        {
            throw new InvalidOperationException($"Full match replay failed: {verified.Message}");
        }
        return new FullMatchResult(state.Outcome, actions.Count, finalHash, replay);
    }

    public static async Task<FullMatchResult> RunSimulationAsync(SimulationFixtureDocument fixture, CardDataSet data,
        IBehaviorCatalog behavior, CancellationToken cancellationToken = default)
    {
        FullMatchDefinition definition = FullMatchDefinition.FromSimulation(fixture, data, behavior);
        if (fixture.Policy is { } policy && (policy.CorpActionOrder.Count > 0 || policy.RunnerActionOrder.Count > 0))
        {
            throw new InvalidOperationException("Opaque simulation action-order overrides are not executable full-game policies.");
        }
        FullMatchResult result = await RunAsync(definition, fixture.ActionCap, cancellationToken: cancellationToken).ConfigureAwait(false);
        SimulationOutcomeExpectation expected = fixture.ExpectedOutcome;
        if (expected.Kind != "won" || result.Outcome.Winner != expected.Winner
            || !string.Equals(result.Outcome.Reason?.ToString(), expected.Reason, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Simulation terminal outcome does not match its declared expectation.");
        }
        return result;
    }
}
