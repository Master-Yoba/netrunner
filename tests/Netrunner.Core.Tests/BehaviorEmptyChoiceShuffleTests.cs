using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Regression coverage for an empty target-selection continuation. An
/// up-to/any-number choice may legally select zero cards, but only the
/// catalog-declared source and owner-correct shuffle destination may consume
/// that empty binding.
/// </summary>
public sealed class BehaviorEmptyChoiceShuffleTests
{
    [Fact]
    public void EmptyArchivesChoiceShufflesCorpDeckAndEmitsZeroCount()
    {
        (BehaviorCatalog catalog, GameState state, CardDefinitionId definitionId, CardInstanceId sourceId) =
            CreateFixture(
                BehaviorChoiceCardinality.UpTo(2),
                BehaviorZone.Deck);
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        GameState result = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);

        Assert.Null(result.PendingDecision);
        // BeginBehaviorForTesting intentionally returns only the immutable
        // state; the Cards real-turn test checks the typed event payload. The
        // event sequence proves exactly one shuffle was emitted here.
        Assert.Equal(state.EventSequence + 1, result.EventSequence);
        Assert.Equal(state.GetZone(ZoneKind.CorpDeck).CardIds, result.GetZone(ZoneKind.CorpDeck).CardIds);
        Assert.Equal(definitionId, result.GetCard(sourceId).DefinitionId);
    }

    [Theory]
    [InlineData(BehaviorChoiceCardinalityKind.Exactly, 1, BehaviorZone.Deck)]
    [InlineData(BehaviorChoiceCardinalityKind.UpTo, 1, BehaviorZone.Stack)]
    public void EmptyChoiceShuffleRejectsForbiddenCardinalityOrOwnerDestination(
        BehaviorChoiceCardinalityKind cardinalityKind,
        int cardinalityCount,
        BehaviorZone destinationZone)
    {
        BehaviorChoiceCardinality cardinality = new(cardinalityKind, cardinalityCount);
        (BehaviorCatalog catalog, GameState state, _, CardInstanceId sourceId) = CreateFixture(cardinality, destinationZone);
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        string before = EngineJson.Serialize(state);

        Assert.Throws<ResolutionFailureException>(() => GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog));
        Assert.Equal(before, EngineJson.Serialize(state));
    }

    private static (BehaviorCatalog Catalog, GameState State, CardDefinitionId DefinitionId, CardInstanceId SourceId)
        CreateFixture(BehaviorChoiceCardinality cardinality, BehaviorZone destinationZone)
    {
        BehaviorCatalog catalog = CreateCatalog(cardinality, destinationZone, out CardDefinitionId definitionId);
        GameState baseline = GameState.CreateNew(
            seed: 44501,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        ZoneState hand = baseline.GetZone(ZoneKind.CorpHand);
        CardInstanceId sourceId = new("card:empty-choice-shuffle-source");
        CardInstanceState source = new(sourceId, definitionId, hand.Id, PlayerSide.Corp, faceUp: false);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == hand.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [sourceId])
                : zone)
            .ToArray();
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            [source],
            zones,
            baseline.Servers,
            baseline.Turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            baseline.DecisionRevision,
            baseline.EventSequence,
            baseline.ResolutionStack,
            baseline.PendingDecision,
            baseline.ActionSequence,
            baseline.InitialSeed,
            baseline.RunState,
            baseline.RulesState,
            catalog.Version,
            catalog.Fingerprint);
        return (catalog, state, definitionId, sourceId);
    }

    private static BehaviorCatalog CreateCatalog(
        BehaviorChoiceCardinality cardinality,
        BehaviorZone destinationZone,
        out CardDefinitionId definitionId)
    {
        definitionId = new("test:empty-choice-shuffle");
        const string choiceKey = "empty-choice-shuffle";
        BehaviorTarget archiveTarget = BehaviorTarget.CardsInZone(
            BehaviorZone.Archives,
            new BehaviorCardFilter(ownerSide: PlayerSide.Corp));
        BehaviorChoiceSpec choice = new(
            choiceKey,
            BehaviorChooser.Controller,
            BehaviorChoiceMode.TargetSelection,
            cardinality,
            target: archiveTarget);
        return new BehaviorCatalogBuilder("test:empty-choice-shuffle")
            .Add(new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Corp,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice),
                    new BehaviorInstruction(
                        BehaviorInstructionKind.ShuffleIntoDeck,
                        target: new BehaviorTarget(BehaviorTargetKind.ChosenCard, selector: choiceKey),
                        sourceZone: BehaviorZone.Archives,
                        destinationZone: destinationZone),
                ]))
            .Build();
    }

}
