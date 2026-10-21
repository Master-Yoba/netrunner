using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class AbilityUsePresentationTests
{
    [Fact]
    public void AOncePerTurnUseInBothPlayersTurnFactsIsShownOnce()
    {
        if (!PlayFormats.IsAvailable(CardFormatIds.NsgCoreSets)) return;
        // NSG Core Sets deck smoke: Madani's once-per-turn install was used on the Runner's turn and
        // again on the Corp's, so the Runner's last-turn facts and the Corp's current-turn facts both
        // held it; the view mapper threw on the "duplicate" and stopped the client.
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("nsg-core-gimbatul~nsg-core-enthusiasm", 47042);
        GameState state = GameEngine.CreateMatch(definition.Request.Seed, definition.Request.CorpDeck,
            definition.Request.RunnerDeck, definition.Request.CardDataVersion, definition.Behavior, definition.Runtime).State;
        Observation observation = ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime);
        string madani = state.Cards.First(card => card.DefinitionId.Value == "35028").Id.Value;
        Dictionary<string, object?> SideFacts() => new()
        {
            ["thisTurn"] = new Dictionary<string, object?>
            {
                ["usedAbilityKeys"] = new object?[]
                {
                    new Dictionary<string, object?> { ["sourceCardId"] = madani, ["abilityKey"] = "madani-install" },
                },
            },
        };
        var publicState = new Dictionary<string, object?>(observation.PublicState)
        {
            ["turnFacts"] = new Dictionary<string, object?> { ["corp"] = SideFacts(), ["runner"] = SideFacts() },
        };

        MatchViewModel view = ObservationViewMapper.Build(observation with { PublicState = publicState },
            definition.Metadata, definition.Behavior);

        AbilityUseViewModel use = Assert.Single(view.UsedAbilityKeys);
        Assert.Equal((madani, "madani-install"), (use.SourceCardId, use.AbilityKey));
    }
}
