using Netrunner.Cards;
using Netrunner.Core;
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
