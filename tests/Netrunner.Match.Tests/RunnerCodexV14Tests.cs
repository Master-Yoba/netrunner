using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Codex reviews of Runner v13, 1 October, in saved ladder games of the current bots:
/// - it declined Zahya Sadeghi's income after accessing cards on HQ, though broke (the trigger scored 0);
/// - it ran HQ through a rezzed Diviner holding only odd-cost cards, where the run ends for certain;
/// - it ran HQ with no credits through a rezzed Ansel 1.0, which trashes a program and forbids stealing.
/// </summary>
public sealed class RunnerCodexV14Tests
{
    [Fact]
    public async Task ZahyaSIncomeIsTaken()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // v13 scored using and declining the same, 0, so its choice was a coin flip (it declined in the game).
        LegalAction chosen = await ChooseAtAsync("codex-runner-zahya", RunnerHeuristicPolicy.V14, 103);
        Assert.True(chosen.Kind == ActionKind.UseTrigger, $"v14 chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V13, true)]
    [InlineData(RunnerHeuristicPolicy.V14, false)]
    public async Task NoRunIntoADivinerThatEndsTheRunForCertain(string version, bool runs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        LegalAction chosen = await ChooseAtAsync("codex-runner-futile-runs", version, 186);
        Assert.True(runs == chosen.Parameters is RunServerParameters { ServerId.Value: "server:4" }, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V13, true)]
    [InlineData(RunnerHeuristicPolicy.V14, false)]
    public async Task NoRunPastAnAnselThatForbidsStealing(string version, bool runs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        LegalAction chosen = await ChooseAtAsync("codex-runner-futile-runs", version, 821);
        Assert.True(runs == chosen.Parameters is RunServerParameters { ServerId.Value: "server:4" }, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    /// <summary>The Runner version's choice at the recorded decision <paramref name="sequence"/>, having watched the game to it.</summary>
    private static async Task<LegalAction> ChooseAtAsync(string fixture, string version, long sequence)
    {
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "saved-games", fixture)));
        GameState state = saved.Replay.InitialSnapshot.ToState();
        OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Corp, "standard", version);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            if (recorded.Sequence == sequence)
            {
                PendingDecision pending = state.PendingDecision!;
                ActionId id = await agent.ChooseActionAsync(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime));
                return pending.LegalActions.Single(action => action.Id == id);
            }
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
            AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        }
        throw new InvalidOperationException($"The game has no action {sequence}.");
    }
}
