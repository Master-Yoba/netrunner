using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class CorpArchetypeTests
{
    [Fact]
    public void TheGatewayCorpDecksDeclareTheirArchetypesAndTheBotPlaysThem()
    {
        if (!Official) return;
        var expected = new Dictionary<string, (CorpArchetype, CorpArchetype)>
        {
            ["gateway-discretion-advised"] = (CorpArchetype.Glacier, CorpArchetype.FastAdvance),
            ["gateway-advanced-yomi"] = (CorpArchetype.ShellGame, CorpArchetype.Horizontal),
            ["gateway-hyper-velocity"] = (CorpArchetype.TagPunish, CorpArchetype.Rush),
            ["gateway-quick-and-dirty"] = (CorpArchetype.Rush, CorpArchetype.TagPunish),
        };
        foreach ((string corp, (CorpArchetype primary, CorpArchetype secondary)) in expected)
        {
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition($"{corp}~gateway-party-hard", 7);
            OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Runner, "standard", CorpHeuristicPolicy.V12);
            Assert.Equal((int)primary, agent.Configuration.Parameters["archetype-primary"]);
            Assert.Equal((int)secondary, agent.Configuration.Parameters["archetype-secondary"]);
        }
        // Versions before v12 don't take an archetype.
        FullMatchDefinition other = RulesFuzzRunner.CreateDefinition("gateway-hyper-velocity~gateway-party-hard", 7);
        Assert.False(OfflineMatchFactory.CreateOpponent(other, PlayerSide.Runner, "standard", CorpHeuristicPolicy.V11)
            .Configuration.Parameters.ContainsKey("archetype-primary"));
    }

    [Fact]
    public void AnUndeclaredDeckGetsItsArchetypesFromItsCards()
    {
        if (!Official) return;
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("gateway-advanced-yomi~gateway-party-hard", 7);
        var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        var inferred = new List<string>();
        foreach (OfficialPreset preset in OfficialPresetCatalog.Embedded().Gateway.Where(preset => preset.Side == PlayerSide.Corp))
        {
            DeckRequest undeclared = new(DeckRequest.CurrentSchemaVersion, PlayerSide.Corp, preset.IdentityCode, preset.Cards, presetId: null);
            (CorpArchetype primary, CorpArchetype secondary) = OfflineMatchFactory.CorpDeckArchetypes(undeclared, catalog);
            inferred.Add($"{preset.PresetId}={primary}/{secondary}");
            Assert.NotEqual(CorpArchetype.Generic, primary);
        }
        // From their cards alone: the trap deck reads as a shell game, the tag deck as tag punishment, and the HB deck as a glacier.
        Assert.Contains(inferred, value => value.StartsWith("gateway-advanced-yomi=ShellGame", StringComparison.Ordinal));
        Assert.Contains(inferred, value => value.StartsWith("gateway-hyper-velocity=TagPunish", StringComparison.Ordinal));
        Assert.Contains(inferred, value => value.StartsWith("gateway-discretion-advised=Glacier", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGatewayRunnerDecksPlayTheirArchetypes()
    {
        if (!Official) return;
        var expected = new Dictionary<string, (RunnerArchetype, RunnerArchetype)>
        {
            ["gateway-party-hard"] = (RunnerArchetype.Virus, RunnerArchetype.BigRig),
            ["gateway-stolen-goods"] = (RunnerArchetype.Aggro, RunnerArchetype.CentralPressure),
            ["gateway-planning-ahead"] = (RunnerArchetype.BigRig, RunnerArchetype.CentralPressure),
        };
        foreach ((string runner, (RunnerArchetype primary, RunnerArchetype secondary)) in expected)
        {
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition($"gateway-hyper-velocity~{runner}", 7);
            OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Corp, "standard", RunnerHeuristicPolicy.V11);
            Assert.Equal((int)primary, agent.Configuration.Parameters["archetype-primary"]);
            Assert.Equal((int)secondary, agent.Configuration.Parameters["archetype-secondary"]);
            // Inferred from the cards alone, the deck gets a real archetype too.
            DeckRequest deck = definition.Request.RunnerDeck;
            (RunnerArchetype inferred, _) = OfflineMatchFactory.RunnerDeckArchetypes(
                new DeckRequest(DeckRequest.CurrentSchemaVersion, PlayerSide.Runner, deck.IdentityCode, deck.Cards),
                new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior));
            Assert.NotEqual(RunnerArchetype.Generic, inferred);
        }
    }

    [Fact]
    public void AnUnknownArchetypeIsRejected()
    {
        string json = """{"schemaVersion":1,"retrievedDate":"x","publishedSourceUrl":"x","metadataCommitSha":"x","provenanceNote":"x","decks":[{"presetId":"p","name":"n","summary":"s","archetypes":["turtle"],"side":"corp","identityCode":"30035","sourceUrl":"u","cards":[]}]}""";
        Assert.ThrowsAny<Exception>(() => OfficialPresetCatalog.ParseGatewayDecks(json));
    }

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;
}
