using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>Bot plan S4.5: the apprentice at the root re-orders only the heuristic's top band.</summary>
public sealed class ApprenticeTests
{
    [Fact]
    public async Task TheApprenticeReordersOnlyTheTopBandAndKeepsEveryPriority()
    {
        // A crafted apprentice that prefers drawing: one weight, on the draw kind.
        int draw = ApprenticeFeatures.Names.ToList().IndexOf($"kind-{ActionKind.DrawCard}");
        int width = ApprenticeFeatures.CombinedNames.Count;
        double[] weights = new double[width];
        weights[draw] = 1;
        var model = new LogisticSideModel(new double[width], [.. Enumerable.Repeat(1.0, width)], weights, 0);
        var document = new ApprenticeDocument(1, "test", ApprenticeFeatures.Names, new ApprenticeSide(model, null), new ApprenticeSide(model, null));
        int checkedPositions = 0;
        foreach ((FullMatchDefinition definition, GameState state, PlayerSide side) in await TurnActionsAsync())
        {
            OfflineAgent agent = (OfflineAgent)RatingLadder.CreateAgent(RatingLadder.ApprenticeBase, side, definition);
            var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
            var keeping = new OfflineAgent(agent.Configuration, OfflinePolicies.Create(agent.Configuration), catalog, null, captureDiagnostics: true);
            AgentLifecycleHost.Observe(keeping, state, definition.Behavior);
            Observation observation = ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime);
            await keeping.ChooseActionAsync(observation);
            var candidates = keeping.LastEvaluation!.Candidates;
            AgentFeatures features = ObservationReader.Read(observation, keeping.Configuration.FeatureVersion);
            Assert.All(features.Actions, action => Assert.Equal(ApprenticeFeatures.Names.Count,
                ApprenticeFeatures.Extract(features, action, candidates, catalog, keeping.Checkpoint.Knowledge).Length));

            var reordered = Apprentice.Reorder(features, candidates, keeping.Checkpoint.Knowledge, catalog, document);

            int top = candidates.Max(candidate => candidate.Utility.Priority);
            Assert.Equal(candidates.Select(candidate => candidate.Utility.Priority), reordered.Select(candidate => candidate.Utility.Priority));
            Assert.Equal(candidates.Where(candidate => candidate.Utility.Priority != top), reordered.Where(candidate => candidate.Utility.Priority != top));
            AgentCandidate first = reordered.Where(candidate => candidate.Utility.Priority == top).MaxBy(candidate => candidate.Utility.Score)!;
            AgentAction[] drawing = [.. features.Actions.Where(action => action.Kind == ActionKind.DrawCard
                && candidates.Single(candidate => candidate.ActionId == action.Id).Utility.Priority == top)];
            if (drawing.Length > 0) Assert.Contains(first.ActionId, drawing.Select(action => action.Id));
            checkedPositions++;
        }
        Assert.True(checkedPositions >= 4, $"Only {checkedPositions} positions.");
        Assert.Equal(1, ((OfflineAgent)RatingLadder.CreateAgent("standard-apprentice", PlayerSide.Runner,
            RulesFuzzRunner.CreateDefinition(Official ? RulesFuzzRunner.OfficialContentIds[0] : RulesFuzzRunner.DeveloperContentId, 41)))
            .Configuration.Parameters[Apprentice.ParameterKey]);
    }

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    /// <summary>Turn actions of either side with a real choice, from random-legal games.</summary>
    private static async Task<List<(FullMatchDefinition, GameState, PlayerSide)>> TurnActionsAsync()
    {
        var positions = new List<(FullMatchDefinition, GameState, PlayerSide)>();
        foreach (ulong seed in new ulong[] { 41, 42 })
        {
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(
                Official ? RulesFuzzRunner.OfficialContentIds[0] : RulesFuzzRunner.DeveloperContentId, seed);
            var corp = new RandomLegalAgent(seed);
            var runner = new RandomLegalAgent(seed + 100);
            GameState state = definition.Start().State;
            for (int count = 0; count < 200 && state.Outcome.Kind == GameOutcomeKind.Ongoing && state.PendingDecision is { } pending; count++)
            {
                PlayerSide owner = state.GetPlayer(pending.Owner).Side;
                if (pending.Kind == DecisionKind.TurnAction && pending.LegalActions.Count > 2 && count % 9 == 0 && positions.Count < 8)
                    positions.Add((definition, state, owner));
                ActionId chosen = await (owner == PlayerSide.Corp ? corp : runner).ChooseActionAsync(ObservationBuilder.Build(state, owner, definition.Behavior));
                state = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, chosen), definition.Behavior, definition.Runtime).State;
            }
        }
        return positions;
    }
}
