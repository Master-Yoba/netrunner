using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Validated immutable composition shared by client and headless execution.</summary>
public sealed class FullMatchDefinition
{
    private FullMatchDefinition(MatchRequest request, CardDataSet data, IBehaviorCatalog behavior)
    {
        Request = request;
        Behavior = behavior;
        Runtime = GatewayRuntimeCardCatalog.Create(data);
        Metadata = new CardCatalogMetadataResolver(data);
    }

    public MatchRequest Request { get; }
    public IBehaviorCatalog Behavior { get; }
    public IRuntimeCardCatalog Runtime { get; }
    public ICardMetadataResolver Metadata { get; }

    public static FullMatchDefinition FromRequest(MatchRequest request, CardDataSet data, IBehaviorCatalog behavior)
    {
        MatchRequestComposer.ValidateUserFacing(request, data, behaviorCatalog: behavior);
        return new FullMatchDefinition(request, data, behavior);
    }

    public static FullMatchDefinition FromSimulation(SimulationFixtureDocument fixture, CardDataSet data, IBehaviorCatalog behavior)
    {
        fixture.ValidateForSimulation(data, behavior);
        return new FullMatchDefinition(new MatchRequest(MatchRequest.CurrentSchemaVersion, fixture.RulesVersion,
            fixture.CardDataVersion, fixture.Seed, fixture.CorpDeck, fixture.RunnerDeck), data, behavior);
    }

    public static FullMatchDefinition Developer(ulong seed = 42)
    {
        DeveloperMatchCards cards = DeveloperMatchCards.Create();
        return FromSimulation(DeveloperFixture(cards, seed), cards.Data, cards.Behavior);
    }

    public static SimulationFixtureDocument DeveloperFixture(DeveloperMatchCards cards, ulong seed = 42) =>
        new(SimulationFixtureDocument.CurrentSchemaVersion, "developer:full-match", EngineCompatibility.RulesVersion,
            cards.Data.Manifest.CommitSha, cards.Behavior.Version, cards.Behavior.Fingerprint,
            seed, 1000, cards.Deck(PlayerSide.Corp), cards.Deck(PlayerSide.Runner),
            new SimulationOutcomeExpectation("won", PlayerSide.Runner, "deckOut"),
            citations: ["CR 1.6", "CR 5.4.1", "CR 5.7.1"],
            crossCheckNote: "Developer-only no-ability identities; complete Gateway subset. Exercises normal setup and full turns, not an official preset.");

    internal GameEngine.TurnStartResult Start() => GameEngine.CreateMatch(Request.Seed, Request.CorpDeck,
        Request.RunnerDeck, Request.CardDataVersion, Behavior, Runtime);
}
