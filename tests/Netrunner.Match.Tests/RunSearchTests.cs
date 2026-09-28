using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class RunSearchTests
{
    [Fact]
    public async Task TheRunnersRunChoicesAreSearchedDeterministically()
    {
        int searched = 0;
        foreach ((FullMatchDefinition definition, GameState state) in await PositionsAsync())
        {
            (ActionId first, IReadOnlyList<string> reasons) = await ChooseAsync(definition, state, RunnerHeuristicPolicy.V9);
            Assert.Contains(reasons, reason => reason.StartsWith("plan/run-search", StringComparison.Ordinal));
            Assert.Contains(state.PendingDecision!.LegalActions, action => action.Id == first);
            // The search depends only on the observation and the checkpoint.
            Assert.Equal(first, (await ChooseAsync(definition, state, RunnerHeuristicPolicy.V9)).Action);
            // The heuristic it builds on doesn't search.
            Assert.DoesNotContain((await ChooseAsync(definition, state, RunnerHeuristicPolicy.V7)).Reasons,
                reason => reason.StartsWith("plan/", StringComparison.Ordinal));
            if (++searched >= 4) break;
        }
        Assert.True(searched > 0, "No Runner run choice to search.");
    }

    [Fact]
    public void RunSearchIsStandardOnlyAndDoesNotPlanTurns()
    {
        OfflineAgentConfiguration standard = OfflinePolicies.Configure(PlayerSide.Runner, "standard", 1, RunnerHeuristicPolicy.V9);
        OfflineAgentConfiguration easy = OfflinePolicies.Configure(PlayerSide.Runner, "easy", 1, RunnerHeuristicPolicy.V9);
        Assert.True(standard.Parameters["run-samples"] > 0 && standard.Parameters["run-width"] > 1);
        Assert.Equal(0, standard.Parameters["plan-samples"]);
        Assert.Equal(0, easy.Parameters["run-samples"]);
    }

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    private static async Task<(ActionId Action, IReadOnlyList<string> Reasons)> ChooseAsync(FullMatchDefinition definition, GameState state, string version)
    {
        OfflineAgent fresh = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Corp, "standard", version);
        OfflineAgentCheckpoint checkpoint = fresh.Checkpoint with { ObservationCursor = state.ActionSequence - 1, ObservationDigest = "position" };
        var agent = new OfflineAgent(fresh.Configuration, OfflinePolicies.Create(fresh.Configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior), checkpoint, captureDiagnostics: true);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        ActionId action = await agent.ChooseActionAsync(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime));
        return (action, [.. agent.LastEvaluation!.Candidates.Select(candidate => candidate.Utility.Reason)]);
    }

    /// <summary>Runner decisions during its runs that offer jacking out or trashing an accessed card, from random-legal games.</summary>
    private static async Task<List<(FullMatchDefinition, GameState)>> PositionsAsync()
    {
        var positions = new List<(FullMatchDefinition, GameState)>();
        foreach (ulong seed in new ulong[] { 41, 42, 43 })
        {
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(
                Official ? RulesFuzzRunner.OfficialContentIds[0] : RulesFuzzRunner.DeveloperContentId, seed);
            var corp = new RandomLegalAgent(seed);
            var runner = new RandomLegalAgent(seed + 100);
            GameState state = definition.Start().State;
            for (int count = 0; count < 600 && state.Outcome.Kind == GameOutcomeKind.Ongoing && state.PendingDecision is { } pending; count++)
            {
                PlayerSide owner = state.GetPlayer(pending.Owner).Side;
                if (owner == PlayerSide.Runner && state.RunState is not null && pending.Kind != DecisionKind.TurnAction
                    && pending.LegalActions.Count > 1
                    && pending.LegalActions.Any(action => action.Kind is ActionKind.JackOut or ActionKind.TrashAccessedCard))
                    positions.Add((definition, state));
                ActionId chosen = await (owner == PlayerSide.Corp ? corp : runner)
                    .ChooseActionAsync(ObservationBuilder.Build(state, owner, definition.Behavior));
                state = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, chosen),
                    definition.Behavior, definition.Runtime).State;
            }
            if (positions.Count >= 4) break;
        }
        return positions;
    }
}
