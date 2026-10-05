using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class TurnPlannerTests
{
    [Theory]
    [InlineData(PlayerSide.Runner, false)]
    [InlineData(PlayerSide.Corp, false)]
    [InlineData(PlayerSide.Runner, true)]
    [InlineData(PlayerSide.Corp, true)]
    public async Task HiddenStateTwinsGetTheSamePlan(PlayerSide planner, bool lines)
    {
        int twins = 0;
        foreach ((FullMatchDefinition definition, GameState state) in await PositionsAsync(planner))
        {
            GameState? twin = SwapTwoHiddenCards(state, planner == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp);
            if (twin is null) continue;
            (ActionId left, bool planned) = await ChooseAsync(definition, state, planner, lines);
            (ActionId right, _) = await ChooseAsync(definition, twin, planner, lines);
            Assert.True(planned, "The planner didn't run on a turn action with a click left.");
            Assert.Equal(left, right);
            // Planning is deterministic from the same checkpoint.
            Assert.Equal(left, (await ChooseAsync(definition, state, planner, lines)).Action);
            if (++twins >= 4) break;
        }
        Assert.True(twins > 0, "No twin positions with a planned turn action.");
    }

    [Theory]
    [InlineData(PlayerSide.Runner)]
    [InlineData(PlayerSide.Corp)]
    public async Task WithoutADeterminizerThePlannerDoesNotRun(PlayerSide side)
    {
        (FullMatchDefinition definition, GameState state) = (await PositionsAsync(side))[0];
        OfflineAgent agent = AgentAt(definition, state, side);

        await agent.ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior));

        Assert.DoesNotContain(agent.LastEvaluation!.Candidates, candidate => candidate.Utility.Priority >= TurnPlannerPriority / 2);
    }

    [Fact]
    public void EasyNeverPlans()
    {
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            OfflineAgentConfiguration easy = OfflinePolicies.Configure(side, "easy", 1, Planning(side));
            Assert.Equal(0, easy.Parameters["plan-samples"]);
            Assert.True(OfflinePolicies.Configure(side, "standard", 1, Planning(side)).Parameters["plan-samples"] > 0);
        }
    }

    private const int TurnPlannerPriority = 1_000_000;

    private static string Planning(PlayerSide side) => side == PlayerSide.Corp ? CorpHeuristicPolicy.V5 : RunnerHeuristicPolicy.V3;

    private static string Live(PlayerSide side) =>
        side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.PolicyVersion;

    private static OfflineAgentConfiguration WithLines(OfflineAgentConfiguration current) => OfflineAgentConfiguration.Create(current.Side,
        current.PolicyVersion, current.ProfileId, current.PolicySeed, current.WorkBudget, current.NearTieTolerance,
        [.. current.Parameters, new KeyValuePair<string, int>("plan-lines", 1)], current.FeatureVersion);

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    private static async Task<(ActionId Action, bool Planned)> ChooseAsync(FullMatchDefinition definition, GameState state, PlayerSide side,
        bool lines = false)
    {
        OfflineAgent agent = AgentAt(definition, state, side, lines);
        ActionId action = await agent.ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime));
        return (action, agent.LastEvaluation!.Candidates.Any(candidate => candidate.Utility.Priority >= TurnPlannerPriority / 2));
    }

    /// <summary>
    /// A fresh Standard agent that has observed <paramref name="state"/> as its first position; with <paramref name="lines"/>,
    /// the live version searching lines of the turn (plan-lines, bot plan S1.5).
    /// </summary>
    private static OfflineAgent AgentAt(FullMatchDefinition definition, GameState state, PlayerSide side, bool lines = false)
    {
        OfflineAgent fresh = OfflineMatchFactory.CreateOpponent(definition, side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp,
            "standard", lines ? Live(side) : Planning(side));
        OfflineAgentCheckpoint checkpoint = state.ActionSequence == 0 ? fresh.Checkpoint
            : fresh.Checkpoint with { ObservationCursor = state.ActionSequence - 1, ObservationDigest = "position" };
        OfflineAgentConfiguration configuration = lines ? WithLines(fresh.Configuration) : fresh.Configuration;
        var agent = new OfflineAgent(configuration, OfflinePolicies.Create(configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior), checkpoint, captureDiagnostics: true);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        return agent;
    }

    /// <summary>Turn actions of <paramref name="side"/> with a click left and a real choice, from random-legal games.</summary>
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
            for (int count = 0; count < 300 && state.Outcome.Kind == GameOutcomeKind.Ongoing && state.PendingDecision is { } pending; count++)
            {
                PlayerSide owner = state.GetPlayer(pending.Owner).Side;
                if (owner == side && pending.Kind == DecisionKind.TurnAction && state.GetPlayer(pending.Owner).Clicks > 0
                    && pending.LegalActions.Count > 1 && count % 3 == 0)
                    positions.Add((definition, state));
                ActionId chosen = await (owner == PlayerSide.Corp ? corp : runner)
                    .ChooseActionAsync(ObservationBuilder.Build(state, owner, definition.Behavior));
                state = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, chosen),
                    definition.Behavior, definition.Runtime).State;
            }
        }
        return positions;
    }

    /// <summary>Swaps the identities of two of <paramref name="owner"/>'s cards in hand or deck, which the other side can't see.</summary>
    internal static GameState? SwapTwoHiddenCards(GameState state, PlayerSide owner)
    {
        Dictionary<ZoneId, ZoneState> zones = state.Zones.ToDictionary(zone => zone.Id);
        ZoneKind hand = owner == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        ZoneKind deck = owner == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack;
        CardInstanceState[] hidden = [.. state.Cards.Where(card => card.OwnerSide == owner && !card.FaceUp
            && (zones[card.ZoneId].Kind == hand || zones[card.ZoneId].Kind == deck))];
        CardInstanceState? first = hidden.FirstOrDefault();
        CardInstanceState? second = hidden.FirstOrDefault(card => first is not null && card.DefinitionId != first.DefinitionId);
        if (first is null || second is null || state.RunState is not null) return null;
        CardInstanceState[] cards = [.. state.Cards.Select(card => card.Id == first.Id ? Recard(card, second.DefinitionId)
            : card.Id == second.Id ? Recard(card, first.DefinitionId) : card)];
        return new GameState(state.SchemaVersion, state.GameId, state.RulesVersion, state.CardDataVersion, state.Players, cards,
            state.Zones, state.Servers, state.Turn, state.Outcome, state.RandomState, state.Allocator, state.DecisionRevision,
            state.EventSequence, state.ResolutionStack, state.PendingDecision, state.ActionSequence, state.InitialSeed,
            state.RunState, state.RulesState, state.BehaviorRegistryVersion, state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion, state.RuntimeCardCatalogFingerprint);
    }

    private static CardInstanceState Recard(CardInstanceState card, CardDefinitionId definition) => new(card.Id, definition,
        card.ZoneId, card.OwnerSide, card.FaceUp, card.Rezzed, card.AdvancementCounters, card.HostCardId);
}
