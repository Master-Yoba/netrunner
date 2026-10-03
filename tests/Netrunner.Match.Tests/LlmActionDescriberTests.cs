using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match.Llm;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class LlmActionDescriberTests
{
    [Fact]
    public void AnIdentitysAbilityIsNamedForTheLlmPlayer()
    {
        if (!PlayFormats.IsAvailable(CardFormatIds.GatewayElevation)) return;
        // Bot loop 1's trial game: Sonnet, playing Topan, saw its identity's ability as "Use a hidden card".
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("elevation-box-nebula-fast-advance~elevation-box-topan", 1);
        GameState state = GameEngine.CreateMatch(definition.Request.Seed, definition.Request.CorpDeck, definition.Request.RunnerDeck,
            definition.Request.CardDataVersion, definition.Behavior, definition.Runtime).State;
        MatchViewModel view = ObservationViewMapper.Build(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime),
            definition.Metadata, definition.Behavior);
        string topan = state.Cards.Single(card => card.DefinitionId.Value == "35002").Id.Value;

        string? described = HumanGameReview.Describer(view)(
            new LegalAction(new ActionId("action:1"), ActionKind.UseAbility, $"Use ability {topan}/topan-install", new NoActionParameters()));

        Assert.Equal("Use ability Topan: Ormas Leader/topan-install", described);
    }
}

public sealed class LlmOpponentViewTests
{
    private sealed class Capture : IStrategicDecider
    {
        public List<string> Requests { get; } = [];

        public Task<StrategicVerdict> DecideAsync(PlayerSide side, string request, int options, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new StrategicVerdict(null, null, null));
        }
    }

    [Fact]
    public async Task TheLlmPlayersViewNamesElevationCards()
    {
        if (!PlayFormats.IsAvailable(CardFormatIds.GatewayElevation)) return;
        // Bot loop 1: the LLM's views read card metadata from the Gateway data, so Sonnet saw Elevation cards as codes.
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("elevation-box-nebula-fast-advance~elevation-box-topan", 1);
        var capture = new Capture();
        LlmOpponentAgent agent = LlmOpponentAgent.Create(PlayerSide.Runner, definition, capture);
        GameState state = GameEngine.CreateMatch(definition.Request.Seed, definition.Request.CorpDeck, definition.Request.RunnerDeck,
            definition.Request.CardDataVersion, definition.Behavior, definition.Runtime).State;
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        for (int step = 0; step < 300 && capture.Requests.Count == 0 && state.PendingDecision is { } decision; step++)
        {
            ActionId id = state.GetPlayer(decision.Owner).Side == PlayerSide.Runner
                ? await agent.ChooseActionAsync(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime))
                : decision.LegalActions[0].Id;
            state = GameEngine.Apply(state, new ActionSubmission(decision.Owner, decision.Id, decision.Revision, id),
                definition.Behavior, definition.Runtime).State;
            AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        }

        string request = Assert.Single(capture.Requests);
        Assert.Contains("Topan", request, StringComparison.Ordinal);
        Assert.DoesNotContain("35002", request, StringComparison.Ordinal);
    }
}
