using Netrunner.AI;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class SimulationApplyTests
{
    [Fact]
    public async Task SimulationMatchesAuthoritativeApplyAcrossAWholeGame()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(4242);
        GameState state = definition.Start().State;
        var corp = new RandomLegalAgent(1);
        var runner = new RandomLegalAgent(2);
        int checkedActions = 0;
        while (state.Outcome.Kind == GameOutcomeKind.Ongoing && state.PendingDecision is { } pending && checkedActions < 400)
        {
            PlayerSide side = state.GetPlayer(pending.Owner).Side;
            ActionId chosen = await (side == PlayerSide.Corp ? corp : runner)
                .ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior));
            var submission = new ActionSubmission(pending.Owner, pending.Id, pending.Revision, chosen);

            Assert.Null(GameEngine.VerifySimulationEquivalence(state, submission, definition.Behavior, definition.Runtime));

            state = GameEngine.Apply(state, submission, definition.Behavior, definition.Runtime).State;
            checkedActions++;
        }

        Assert.True(checkedActions > 20);
    }

    [Fact]
    public void SimulatedStatesCannotLeaveSimulation()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(4243);
        GameState state = definition.Start().State;
        PendingDecision pending = state.PendingDecision!;
        var submission = new ActionSubmission(pending.Owner, pending.Id, pending.Revision, pending.LegalActions[0].Id);

        ApplyResult simulated = GameEngine.Apply(state, submission, definition.Behavior, definition.Runtime, ApplyMode.Simulation);

        Assert.True(simulated.Accepted);
        Assert.True(simulated.State.IsSimulated);
        Assert.Null(simulated.AcceptedAction);
        Assert.False(state.IsSimulated);
        Assert.Throws<InvalidOperationException>(() => EngineJson.Serialize(simulated.State));
        Assert.Throws<InvalidOperationException>(() => simulated.State.CreateSnapshot(definition.Behavior, definition.Runtime));
        Assert.Throws<InvalidOperationException>(() => DeterministicHash.ForState(simulated.State));

        PendingDecision next = simulated.State.PendingDecision!;
        var nextSubmission = new ActionSubmission(next.Owner, next.Id, next.Revision, next.LegalActions[0].Id);
        ApplyResult authoritative = GameEngine.Apply(simulated.State, nextSubmission, definition.Behavior, definition.Runtime);
        Assert.False(authoritative.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, authoritative.Rejection?.Code);

        ApplyResult continued = GameEngine.Apply(simulated.State, nextSubmission, definition.Behavior, definition.Runtime,
            ApplyMode.Simulation);
        Assert.True(continued.Accepted);
        Assert.True(continued.State.IsSimulated);
    }
}
