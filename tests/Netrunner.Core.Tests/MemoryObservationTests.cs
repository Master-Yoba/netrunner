using Xunit;

namespace Netrunner.Core.Tests;

public sealed class MemoryObservationTests
{
    [Fact]
    public void RunnerMemoryStartsAtZeroOfFourOnlyWhenRulesStateIsPresent()
    {
        GameState legacy = GameState.CreateNew(seed: 8101);
        Dictionary<string, object?> legacyRunner = Player(
            ObservationBuilder.Build(legacy, PlayerSide.Runner),
            "runner");

        Assert.False(legacyRunner.ContainsKey("usedMemory"));
        Assert.False(legacyRunner.ContainsKey("memoryLimit"));

        GameState withRules = WithRulesState(legacy, RulesState.CreateDefault(legacy.Players));
        foreach (PlayerSide viewer in Enum.GetValues<PlayerSide>())
        {
            Observation observation = ObservationBuilder.Build(withRules, viewer);
            Dictionary<string, object?> runner = Player(observation, "runner");
            Dictionary<string, object?> corp = Player(observation, "corp");

            Assert.Equal(0, runner["usedMemory"]);
            Assert.Equal(4, runner["memoryLimit"]);
            Assert.False(corp.ContainsKey("usedMemory"));
            Assert.False(corp.ContainsKey("memoryLimit"));
        }
    }

    [Fact]
    public void RunnerMemoryLimitIncludesActiveStaticMemoryModifier()
    {
        GameState baseline = GameState.CreateNew(seed: 8102);
        PlayerState[] players = baseline.Players.ToArray();
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        CardDefinitionId modifierDefinition = new("test:memory-observation-modifier");
        CardInstanceId modifierCard = new("card:memory-observation-modifier");
        CardInstanceState card = new(
            modifierCard,
            modifierDefinition,
            rig.Id,
            PlayerSide.Runner,
            faceUp: true);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [modifierCard])
                : zone)
            .ToArray();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test:memory-observation")
            .Add(new CardBehaviorDefinition(
                modifierDefinition,
                PlayerSide.Runner,
                staticModifiers:
                [new BehaviorStaticModifier(BehaviorStaticModifierKind.MemoryUnits, 2)]))
            .Build();
        GameState state = WithRulesState(
            baseline,
            RulesState.CreateDefault(players),
            cards: [card],
            zones: zones);

        Dictionary<string, object?> runner = Player(
            ObservationBuilder.Build(state, PlayerSide.Runner, behaviorCatalog),
            "runner");

        Assert.Equal(0, runner["usedMemory"]);
        Assert.Equal(6, runner["memoryLimit"]);
    }

    private static Dictionary<string, object?> Player(Observation observation, string side)
    {
        IReadOnlyDictionary<string, object?> players = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            observation.PublicState["players"]);
        return Assert.IsType<Dictionary<string, object?>>(players[side]);
    }

    private static GameState WithRulesState(
        GameState state,
        RulesState rulesState,
        IReadOnlyList<CardInstanceState>? cards = null,
        IReadOnlyList<ZoneState>? zones = null) =>
        new(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            players: state.Players,
            cards: cards ?? state.Cards,
            zones: zones ?? state.Zones,
            servers: state.Servers,
            turn: state.Turn,
            outcome: state.Outcome,
            randomState: state.RandomState,
            allocator: state.Allocator,
            schemaVersion: state.SchemaVersion,
            decisionRevision: state.DecisionRevision,
            eventSequence: state.EventSequence,
            resolutionStack: state.ResolutionStack,
            pendingDecision: state.PendingDecision,
            actionSequence: state.ActionSequence,
            initialSeed: state.InitialSeed,
            runState: state.RunState,
            rulesState: rulesState,
            behaviorRegistryVersion: state.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: state.BehaviorCatalogFingerprint,
            runtimeCardCatalogVersion: state.RuntimeCardCatalogVersion,
            runtimeCardCatalogFingerprint: state.RuntimeCardCatalogFingerprint);
}
