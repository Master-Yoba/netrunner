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
        foreach ((FullMatchDefinition definition, GameState state) in await PositionsAsync(PlayerSide.Runner))
        {
            (ActionId first, IReadOnlyList<string> reasons) = await ChooseAsync(definition, state, RunnerHeuristicPolicy.V9, PlayerSide.Runner);
            Assert.Contains(reasons, reason => reason.StartsWith("plan/run-search", StringComparison.Ordinal));
            Assert.Contains(state.PendingDecision!.LegalActions, action => action.Id == first);
            // The search depends only on the observation and the checkpoint.
            Assert.Equal(first, (await ChooseAsync(definition, state, RunnerHeuristicPolicy.V9, PlayerSide.Runner)).Action);
            // The heuristic it builds on doesn't search.
            Assert.DoesNotContain((await ChooseAsync(definition, state, RunnerHeuristicPolicy.V7, PlayerSide.Runner)).Reasons,
                reason => reason.StartsWith("plan/", StringComparison.Ordinal));
            if (++searched >= 4) break;
        }
        Assert.True(searched > 0, "No Runner run choice to search.");
    }

    [Fact]
    public async Task TheCorpsRezDuringARunIsSearched()
    {
        int searched = 0;
        foreach ((FullMatchDefinition definition, GameState state) in await PositionsAsync(PlayerSide.Corp))
        {
            (ActionId first, IReadOnlyList<string> reasons) = await ChooseAsync(definition, state, CorpHeuristicPolicy.V10, PlayerSide.Corp);
            Assert.Contains(reasons, reason => reason.StartsWith("plan/run-search", StringComparison.Ordinal));
            Assert.Equal(first, (await ChooseAsync(definition, state, CorpHeuristicPolicy.V10, PlayerSide.Corp)).Action);
            if (++searched >= 3) break;
        }
        Assert.True(searched > 0, "No Corp rez during a run to search.");
    }

    [Fact]
    public void RunSearchIsStandardOnlyAndDoesNotPlanTurns()
    {
        OfflineAgentConfiguration standard = OfflinePolicies.Configure(PlayerSide.Runner, "standard", 1, RunnerHeuristicPolicy.V9);
        OfflineAgentConfiguration easy = OfflinePolicies.Configure(PlayerSide.Runner, "easy", 1, RunnerHeuristicPolicy.V9);
        Assert.True(standard.Parameters["run-samples"] > 0 && standard.Parameters["run-width"] > 1);
        Assert.Equal(0, standard.Parameters["plan-samples"]);
        Assert.Equal(0, easy.Parameters["run-samples"]);
        OfflineAgentConfiguration corp = OfflinePolicies.Configure(PlayerSide.Corp, "standard", 1, CorpHeuristicPolicy.V10);
        Assert.True(corp.Parameters["run-samples"] > 0 && corp.Parameters["plan-samples"] == 0);
        Assert.Equal(0, OfflinePolicies.Configure(PlayerSide.Corp, "easy", 1, CorpHeuristicPolicy.V10).Parameters["run-samples"]);
    }

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    private static async Task<(ActionId Action, IReadOnlyList<string> Reasons)> ChooseAsync(FullMatchDefinition definition, GameState state, string version,
        PlayerSide side)
    {
        OfflineAgent fresh = OfflineMatchFactory.CreateOpponent(definition, side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp,
            "standard", version);
        OfflineAgentCheckpoint checkpoint = fresh.Checkpoint with { ObservationCursor = state.ActionSequence - 1, ObservationDigest = "position" };
        var agent = new OfflineAgent(fresh.Configuration, OfflinePolicies.Create(fresh.Configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior), checkpoint, captureDiagnostics: true);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        ActionId action = await agent.ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime));
        return (action, [.. agent.LastEvaluation!.Candidates.Select(candidate => candidate.Utility.Reason)]);
    }

    /// <summary>
    /// Decisions during runs, from random-legal games: the Runner's that offer
    /// jacking out or trashing an accessed card, or the Corp's that offer a rez.
    /// </summary>
    private static async Task<List<(FullMatchDefinition, GameState)>> PositionsAsync(PlayerSide side)
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
                if (owner == side && state.RunState is not null && pending.Kind != DecisionKind.TurnAction
                    && pending.LegalActions.Count > 1
                    && pending.LegalActions.Any(action => side == PlayerSide.Runner
                        ? action.Kind is ActionKind.JackOut or ActionKind.TrashAccessedCard
                        : action.Kind is ActionKind.RezCard or ActionKind.RezIce))
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
