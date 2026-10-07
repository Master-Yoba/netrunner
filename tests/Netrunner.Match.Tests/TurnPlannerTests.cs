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

    /// <summary>
    /// Bot plan S4.2 (S5.1's scoring, plan-value 1): planned candidates are ranked by their value in win probability, and
    /// the chosen one is the heuristic's first within plan-tie (0.005) of the best; hidden-state twins still get one plan.
    /// </summary>
    [Theory]
    [InlineData(PlayerSide.Runner)]
    [InlineData(PlayerSide.Corp)]
    public async Task ValueScoringChoosesTheHeuristicsFirstWithinTheTieOfTheBestValue(PlayerSide side)
    {
        int checkedPositions = 0;
        foreach ((FullMatchDefinition definition, GameState state) in (await PositionsAsync(side)).Take(6))
        {
            OfflineAgent agent = AgentAt(definition, state, side, value: true);
            ActionId chosen = await agent.ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime));
            AgentCandidate[] planned = [.. agent.LastEvaluation!.Candidates.Where(candidate => candidate.Utility.Priority >= TurnPlannerPriority / 2)];
            if (planned.Length < 2) continue;
            int top = planned.Max(candidate => candidate.Utility.Priority);
            AgentCandidate[] band = [.. planned.Where(candidate => candidate.Utility.Priority == top)];
            double Value(AgentCandidate candidate) => double.Parse(System.Text.RegularExpressions.Regex.Match(candidate.Utility.Reason,
                @" value ([+-][0-9.]+) ").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.All(planned, candidate => Assert.InRange(Value(candidate), -2, 1));
            AgentCandidate winner = band.Single(candidate => candidate.ActionId == chosen);
            Assert.True(Value(winner) >= band.Max(Value) - 0.0051, $"{Value(winner)} against the best {band.Max(Value)}");
            Assert.Equal(band.Max(candidate => candidate.Utility.Score), winner.Utility.Score);
            Assert.Equal(chosen, await AgentAt(definition, state, side, value: true)
                .ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime)));
            checkedPositions++;
        }
        Assert.True(checkedPositions > 0, "No position with two planned candidates.");
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

    private static OfflineAgentConfiguration With(OfflineAgentConfiguration current, string key) => OfflineAgentConfiguration.Create(current.Side,
        current.PolicyVersion, current.ProfileId, current.PolicySeed, current.WorkBudget, current.NearTieTolerance,
        [.. current.Parameters, new KeyValuePair<string, int>(key, 1)], current.FeatureVersion);

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
    /// the live Hard bot searching lines of the turn (plan-lines, bot plan S1.5; the live Runner's Standard does not plan);
    /// with <paramref name="value"/>, the live Hard bot valuing candidates in win probability (plan-value, S4.2).
    /// </summary>
    private static OfflineAgent AgentAt(FullMatchDefinition definition, GameState state, PlayerSide side, bool lines = false, bool value = false)
    {
        bool live = lines || value;
        OfflineAgent fresh = OfflineMatchFactory.CreateOpponent(definition, side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp,
            live ? "hard" : "standard", live ? Live(side) : Planning(side));
        OfflineAgentConfiguration configuration = lines ? With(fresh.Configuration, "plan-lines")
            : value ? With(fresh.Configuration, "plan-value") : fresh.Configuration;
        // A checkpoint belongs to its configuration, so the switched-on agent starts from its own.
        OfflineAgentCheckpoint start = live ? new OfflineAgent(configuration, OfflinePolicies.Create(configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior)).Checkpoint : fresh.Checkpoint;
        OfflineAgentCheckpoint checkpoint = state.ActionSequence == 0 ? start
            : start with { ObservationCursor = state.ActionSequence - 1, ObservationDigest = "position" };
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
